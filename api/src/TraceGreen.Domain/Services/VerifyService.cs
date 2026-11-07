using System.Globalization;
using System.Text.Json.Nodes;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Plausibility;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Security;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Trust;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Ruling 5 maps these to HTTP: <see cref="InvalidToken"/> 401, <see cref="Expired"/> and <see cref="InvoiceChanged"/>
/// 410, <see cref="AlreadyUsed"/> 409, <see cref="InvalidAnswer"/>, <see cref="InvalidEvidence"/> and <see cref="TermsNotAccepted"/> 422,
/// <see cref="NodeNotFound"/> 404 (dev path only). <see cref="NodeChanged"/> 409 comes only from a submit that passes the
/// node state it expects (the supplier page, F12).
/// </summary>
/// <see cref="InvalidEvidence"/> is optional proof that cannot be right (F7): the answers themselves were fine, so a page keeps
/// them and asks only for the proof to be fixed or left out.
public enum VerifyErrorCode { InvalidToken, Expired, InvoiceChanged, AlreadyUsed, InvalidAnswer, NodeNotFound, TermsNotAccepted, NodeChanged, InvalidEvidence }

public sealed record VerifyError(VerifyErrorCode Code, string Message);

/// <summary>
/// Where an answer's link came from, for a caller that mints the <c>v1.</c> link itself (the supplier page): when the link
/// the supplier opened was made, and the keyed hash of its maker's address (null when unknown).
/// </summary>
public sealed record AnswerOrigin(DateTimeOffset IssuedAt, string? MakerIpHash);

/// <summary>
/// The anonymous supplier side of the loop (F3). A token is checked in the signer's order (format, signature, expiry,
/// invoice hash recomputed from the stored extract), then for single use. A submit then needs the current supplier terms'
/// SHA-256 (the supplier ticked them), then valid answers. It rebuilds the node from the answers
/// through <see cref="NodeFactory"/>, records the verification and its audit entry atomically (the repository's unique
/// nonce turns a replay into <see cref="VerifyErrorCode.AlreadyUsed"/>), and publishes <c>node.verified</c> with fresh
/// KPIs, then <c>audit.appended</c>. Neither the token, its nonce nor the client address reaches a payload or an event.
/// <para>
/// F7: an answer may carry evidence (a shipping document for the mode, the dispatch date, a certificate for renewable
/// power), checked here for shape and check digit. The node keeps it with how the answer arrived (from the link maker's
/// network, how soon after the link, the supplier's history) as its <see cref="TrustInputs"/>, from which
/// <see cref="TrustAssessor"/> computes the counted figure and the score.
/// </para>
/// </summary>
public sealed class VerifyService(
    IRepository repo,
    IEventBus bus,
    ILinkSigner signer,
    LinkOriginStore origins,
    SeedBundle seed,
    NodeViewMapper mapper,
    GraphService graph,
    TimeProvider clock,
    ServiceOptions options)
{
    /// <summary>Real tokens are about 190 characters; anything far longer is refused before it is parsed.</summary>
    public const int MaxTokenLength = 512;

    /// <summary>Keeps a confirmed mass inside the bounds ingest applies to quantities, so later sums cannot overflow.</summary>
    public const decimal MaxMassT = 1_000_000m;

    private const string Actor = "supplier";

    private sealed record Link(Ctx Ctx, SupplierNode Node, Invoice Invoice, Lane Lane, string Nonce, DateTimeOffset ExpiresAt);

    public async Task<Result<VerifyContext, VerifyError>> ContextAsync(string? token, CancellationToken ct)
    {
        var opened = await OpenAsync(token, ct);
        if (!opened.IsOk)
        {
            return Result<VerifyContext, VerifyError>.Fail(opened.Error);
        }
        var link = opened.Value;
        if (await IsUsedAsync(link, ct))
        {
            return Result<VerifyContext, VerifyError>.Fail(Used());
        }

        var buyer = await repo.GetBuyerAsync(link.Ctx, ct);
        var line = InvoiceFacts.DominantLine(link.Invoice.Extracted.Lines);
        return Result<VerifyContext, VerifyError>.Ok(new VerifyContext(
            NodeId: link.Node.Id,
            SupplierLabel: link.Node.Label,
            BuyerName: buyer.Name,
            InvoiceNumber: link.Invoice.Extracted.Number,
            IssueDate: link.Invoice.IssueDate,
            ItemDescription: line.Description,
            Origin: link.Lane.Origin,
            Dest: link.Lane.Dest,
            Modes: Simulator.AvailableModes(link.Lane),
            DefaultMode: link.Lane.ModeDefault,
            EnergySources: Enum.GetValues<EnergySource>(),
            DefaultEnergy: EnergySource.Grid,
            MassT: link.Node.MassT,
            ExpiresAt: link.ExpiresAt,
            Terms: VerifyTerms.From(seed.SupplierTerms)));
    }

    /// <param name="clientAddress">Hashed with <see cref="ServiceOptions.IpHashKey"/> before it is stored; never stored as is.</param>
    public Task<Result<VerifyOutcome, VerifyError>> SubmitAsync(VerifyAnswer answer, string? clientAddress, CancellationToken ct) =>
        SubmitAsync(answer, clientAddress, expected: null, origin: null, ct);

    /// <param name="clientAddress">Hashed with <see cref="ServiceOptions.IpHashKey"/> before it is stored; never stored as is.</param>
    /// <param name="expected">
    /// The node's status and answers the caller built this answer on. The repository compares them under the write's lock and
    /// refuses with <see cref="VerifyErrorCode.NodeChanged"/> when another answer landed first. Null for the phone page.
    /// </param>
    /// <param name="origin">The link the supplier opened, when the caller minted this one itself; null takes this link's own.</param>
    public async Task<Result<VerifyOutcome, VerifyError>> SubmitAsync(
        VerifyAnswer answer, string? clientAddress, NodeAnswerState? expected, AnswerOrigin? origin, CancellationToken ct)
    {
        var opened = await OpenAsync(answer.Token, ct);
        if (!opened.IsOk)
        {
            return Fail(opened.Error);
        }
        var link = opened.Value;
        // Checked before the answers, so scanning a used link again says "already used" whatever is sent.
        if (await IsUsedAsync(link, ct))
        {
            return Fail(Used());
        }
        // Before the answers: without the terms the supplier saw and ticked, nothing they said is recorded.
        var terms = seed.SupplierTerms;
        if (!string.Equals(answer.AcceptedTerms, terms.Sha256, StringComparison.Ordinal))
        {
            return Fail(new VerifyError(
                VerifyErrorCode.TermsNotAccepted,
                answer.AcceptedTerms is null
                    ? "Accept the supplier terms before submitting."
                    : $"These are not the current supplier terms (version {terms.Version}); reload the page."));
        }
        var now = clock.GetUtcNow();
        if (Invalid(answer, link.Lane) is { } problem)
        {
            return Fail(new VerifyError(VerifyErrorCode.InvalidAnswer, problem));
        }
        // Cleaned first (ids trimmed and normalised, blanks dropped), so what is checked is what is stored.
        var evidence = Normalized(answer.Evidence);
        if (InvalidEvidence(evidence, answer, link.Invoice, now) is { } proofProblem)
        {
            return Fail(new VerifyError(VerifyErrorCode.InvalidEvidence, proofProblem));
        }

        var node = link.Node;
        var status = Estimate.DeriveStatus(answer.TransportMode, answer.EnergySource);
        var answerIpHash = Hashing.IpHash(clientAddress ?? "unknown", options.IpHashKey);
        var context = await AnswerContextAsync(link, answer, origin, clientAddress is null ? null : answerIpHash, now, ct);
        var invoiceMassT = InvoiceFacts.MassTFromLines(link.Invoice.Extracted.Lines);

        // Computed by the repository from the node as it is under the write's lock, so a buyer's record, a decision or a
        // category review that landed after this request read the node is kept, and the figures rest on it.
        IReadOnlyList<PlausibilityWarning> warnings = [];
        TrustAssessment? assessment = null;
        NodePatch Patch(SupplierNode current)
        {
            var trust = TrustAfter(current, answer, evidence, context);
            var rebuilt = NodeFactory.NewNode(
                current.Id, link.Invoice,
                new NodeClassification(current.CategoryId, current.AiConfidence, current.ClassificationSource, current.ClassificationRationale), link.Lane,
                status, answer.TransportMode, answer.EnergySource, seed, mapper.Factors, current.CreatedAt, answer.MassT ?? current.MassT, trust);
            warnings = PlausibilityRules.For(rebuilt, invoiceMassT, link.Lane);
            assessment = TrustAssessor.Assess(rebuilt, link.Invoice, link.Lane, seed.CategoryOf(rebuilt.CategoryId), mapper.Factors)?.Assessment;
            return new NodePatch(
                status, rebuilt.ActivityKg, answer.TransportMode, answer.EnergySource, rebuilt.MassT, rebuilt.CostEur, rebuilt.LeadDays,
                rebuilt.Trust, rebuilt.CountedKg);
        }

        var verification = new Verification(
            Ids.NewId(), node.WorkspaceId, node.Id, link.Nonce, answer.TransportMode, answer.EnergySource, answer.MassT, now,
            answerIpHash, now, terms.Version, terms.Sha256, evidence);
        // Strings only (see IngestService): jsonb may re-render decimals and break the chain hash. No nonce, no address.
        // Built when the audit entry is, right after the patch under the same lock, so the warnings and score are the patch's.
        JsonObject Payload()
        {
        var payload = new JsonObject
        {
            ["nodeId"] = node.Id.ToString("D"),
            ["verificationId"] = verification.Id.ToString("D"),
            ["status"] = status.ToString().ToUpperInvariant(),
            ["transportMode"] = answer.TransportMode?.ToString().ToLowerInvariant(),
            ["energySource"] = answer.EnergySource?.ToString().ToLowerInvariant(),
            ["massTConfirmed"] = answer.MassT?.ToString(CultureInfo.InvariantCulture),
            ["warnings"] = new JsonArray(warnings.Select(w => (JsonNode)JsonValue.Create(Name(w.Code))).ToArray()),
            ["termsVersion"] = terms.Version,
            ["termsSha256"] = terms.Sha256,
        };
        // F7: what the supplier gave to back the answers, so the claim and its document are in the tamper-evident log, and
        // the score it got. Business identifiers only: no token, nonce or address.
        if (evidence is not null)
        {
            payload["shippingDocType"] = evidence.ShippingDocType is { } docType ? TrustAssessor.Camel(docType) : null;
            payload["shippingDocId"] = evidence.ShippingDocId;
            payload["dispatchDate"] = evidence.DispatchDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            payload["energyCertType"] = evidence.EnergyCertType is { } certType ? TrustAssessor.Camel(certType) : null;
            payload["energyCertId"] = evidence.EnergyCertId;
        }
        if (assessment is not null)
        {
            payload["trustScore"] = assessment.Score.ToString(CultureInfo.InvariantCulture);
            payload["trustBand"] = TrustAssessor.Camel(assessment.Band);
            payload["trustSignals"] = new JsonArray(assessment.Signals.Select(sig => (JsonNode)JsonValue.Create(SignalName(sig.Code))).ToArray());
        }
        if (answer.Simulated)
        {
            // The terms were accepted by the demo tool, not by a person at the supplier; the log must not pass it off as one.
            payload["simulated"] = true;
        }
        return payload;
        }
        var recorded = await repo.RecordVerificationAsync(
            link.Ctx, verification, Patch, prev => AuditChain.Build(prev, link.Ctx.WorkspaceId, now, Actor, "verification.submitted", Payload()),
            expected, ct);
        if (recorded.Status == RecordVerificationStatus.NodeChanged)
        {
            return Fail(new VerifyError(VerifyErrorCode.NodeChanged, "Another answer for this item arrived first."));
        }
        if (recorded.Status == RecordVerificationStatus.AlreadyUsed || recorded is not { Node: { } patched, Entry: { } entry })
        {
            return Fail(Used());
        }

        var view = mapper.Map(patched, link.Invoice);
        var kpis = await graph.KpisAsync(link.Ctx, ct);
        var verificationView = new VerificationView(verification.Id, verification.TransportMode, verification.EnergySource, verification.MassTConfirmed, verification.ConsumedAt);
        bus.Publish(link.Ctx.WorkspaceId, (seq, ts) => new NodeVerified(seq, ts, link.Ctx.WorkspaceId, view, verificationView, kpis));
        bus.Publish(link.Ctx.WorkspaceId, (seq, ts) => new AuditAppended(seq, ts, link.Ctx.WorkspaceId, entry));
        return Result<VerifyOutcome, VerifyError>.Ok(new VerifyOutcome(node.Id, status, warnings));
    }

    /// <summary>
    /// Finds the node the token names, then lets the signer decide. A node that no longer exists (a demo reset removed it)
    /// is checked against a hash no token carries, so a forged token still fails on its signature (401) and only a genuine
    /// one reaches <see cref="VerifyErrorCode.InvoiceChanged"/> (410).
    /// </summary>
    private async Task<Result<Link, VerifyError>> OpenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength || !signer.TryReadNodeId(token, out var nodeId))
        {
            return Result<Link, VerifyError>.Fail(Invalid());
        }

        var node = await repo.FindNodeAsync(nodeId, ct);
        var ctx = new Ctx(node?.WorkspaceId ?? Guid.Empty, Actor);
        var invoice = node is null ? null : await repo.GetInvoiceAsync(ctx, node.InvoiceId, ct);
        // Recomputed from the stored extract rather than read from the stored hash, so a changed invoice row is caught (C2).
        var expectedHash = invoice is null ? string.Empty : Hashing.InvoiceHash(invoice.Extracted);
        var verified = signer.Verify(token, expectedHash, clock.GetUtcNow());
        if (!verified.IsOk)
        {
            return Result<Link, VerifyError>.Fail(verified.Error switch
            {
                TokenError.Expired => new VerifyError(VerifyErrorCode.Expired, "This link has expired."),
                TokenError.HashMismatch => new VerifyError(VerifyErrorCode.InvoiceChanged, "The invoice changed after this link was made."),
                _ => Invalid(),
            });
        }

        var lane = seed.Lanes.FirstOrDefault(l => l.Id == node!.LaneId);
        if (node is null || invoice is null || lane is null)
        {
            return Result<Link, VerifyError>.Fail(new VerifyError(VerifyErrorCode.InvoiceChanged, "The item this link was made for is no longer on the graph."));
        }
        return Result<Link, VerifyError>.Ok(new Link(ctx, node, invoice, lane, verified.Value.Nonce, verified.Value.ExpiresAt));
    }

    private async Task<bool> IsUsedAsync(Link link, CancellationToken ct) =>
        (await repo.ListVerificationsAsync(link.Ctx, link.Node.Id, ct)).Any(v => string.Equals(v.TokenNonce, link.Nonce, StringComparison.Ordinal));

    private static string? Invalid(VerifyAnswer answer, Lane lane)
    {
        if (answer.TransportMode is null && answer.EnergySource is null)
        {
            return "Answer at least one question.";
        }
        if (answer.EnergySource is { } energy && !Enum.IsDefined(energy))
        {
            return "Unknown power source.";
        }
        if (answer.TransportMode is { } mode && !Simulator.AvailableModes(lane).Contains(mode))
        {
            return $"Lane {lane.Id} has no {Name(mode)} leg.";
        }
        if (answer.MassT is { } massT && (massT <= 0m || massT > MaxMassT))
        {
            return $"The mass must be above 0 and at most {MaxMassT:0} t.";
        }
        return null;
    }

    /// <summary>How the answer arrived: from the link maker's network, how soon after the link, and the supplier's history.</summary>
    private async Task<AnswerContext> AnswerContextAsync(
        Link link, VerifyAnswer answer, AnswerOrigin? origin, string? answerIpHash, DateTimeOffset now, CancellationToken ct)
    {
        var issuedAt = origin?.IssuedAt ?? link.ExpiresAt - options.LinkTtl;
        var makerIpHash = origin is null ? origins.MakerOf(LinkOriginStore.VerifyKey(link.Nonce)) : origin.MakerIpHash;
        var (modeChangedOnLane, priorRejection) = await HistoryAsync(link, answer.TransportMode, ct);
        return new AnswerContext(
            SameNetworkAsLinkMaker: makerIpHash is null || answerIpHash is null ? null : string.Equals(makerIpHash, answerIpHash, StringComparison.Ordinal),
            LinkAgeSeconds: (int)Math.Clamp((now - issuedAt).TotalSeconds, 0, int.MaxValue),
            ModeChangedOnLane: modeChangedOnLane,
            PriorRejection: priorRejection,
            Simulated: answer.Simulated);
    }

    /// <summary>
    /// What the trust assessment reads after this answer, from the node as it is under the write's lock: the evidence (what
    /// this answer gives, plus what an earlier answer gave for a question it leaves as it was), how the answer arrived, the
    /// buyer's records as they are, and a person's decisions, each dropped when the answer it was about changes.
    /// </summary>
    private static TrustInputs TrustAfter(SupplierNode current, VerifyAnswer answer, AnswerEvidence? evidence, AnswerContext context)
    {
        var before = current.Trust ?? TrustInputs.None;
        var modeChanged = current.TransportMode != answer.TransportMode;
        var energyChanged = current.EnergySource != answer.EnergySource;
        var massChanged = answer.MassT is not null && answer.MassT != current.MassT;
        return before with
        {
            Evidence = AnswerEvidence.Merge(before.Evidence, evidence, modeChanged, energyChanged),
            Answer = context,
            ModeReview = modeChanged ? null : before.ModeReview,
            EnergyReview = energyChanged ? null : before.EnergyReview,
            MassReview = massChanged ? null : before.MassReview,
        };
    }

    /// <summary>
    /// The same supplier's other deliveries (same seller tax id, or name when there is none): whether the last one on this
    /// lane went by another mode, and whether a person rejected any of their answers.
    /// </summary>
    private async Task<(bool ModeChangedOnLane, bool PriorRejection)> HistoryAsync(Link link, TransportMode? mode, CancellationToken ct)
    {
        var key = PortalService.SupplierKey(link.Node, link.Invoice);
        var others = new List<SupplierNode>();
        foreach (var other in await repo.ListNodesAsync(link.Ctx, ct))
        {
            if (other.Id == link.Node.Id || (other.TransportMode is null && other.Trust?.HasRejection != true))
            {
                continue;
            }
            if (await repo.GetInvoiceAsync(link.Ctx, other.InvoiceId, ct) is { } invoice && PortalService.SupplierKey(other, invoice) == key)
            {
                others.Add(other);
            }
        }
        var lastOnLane = others.Where(o => o.LaneId == link.Node.LaneId && o.TransportMode is not null).MaxBy(o => o.CreatedAt);
        var modeChanged = mode is not null && lastOnLane is not null && lastOnLane.TransportMode != mode;
        return (modeChanged, others.Any(o => o.Trust?.HasRejection == true));
    }

    /// <summary>
    /// The evidence with blank ids dropped and the rest trimmed and normalised (uppercase, no spaces), or null when nothing
    /// is left, so proof made of spaces leaves no trace in the row or the audit entry.
    /// </summary>
    private static AnswerEvidence? Normalized(AnswerEvidence? evidence)
    {
        if (evidence is null)
        {
            return null;
        }
        static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        var docId = Clean(evidence.ShippingDocId);
        var cleaned = evidence with
        {
            ShippingDocId = evidence.ShippingDocType is { } type && docId is { } id ? DocumentIds.Normalize(type, id) : docId,
            EnergyCertId = Clean(evidence.EnergyCertId),
        };
        return cleaned.IsEmpty ? null : cleaned;
    }

    /// <summary>
    /// Why the (cleaned) evidence cannot be recorded with this answer, or null. The page checks the same rules before it
    /// sends. A type outside its list (a number where a name belongs) is refused too.
    /// </summary>
    private static string? InvalidEvidence(AnswerEvidence? evidence, VerifyAnswer answer, Invoice invoice, DateTimeOffset now)
    {
        if (evidence is null)
        {
            return null;
        }
        if (evidence.ShippingDocType is { } shownType && !Enum.IsDefined(shownType)
            || evidence.EnergyCertType is { } certKind && !Enum.IsDefined(certKind))
        {
            return "Unknown document or certificate type.";
        }
        if ((evidence.ShippingDocType is null) == (evidence.ShippingDocId is not null))
        {
            return "Give the shipping document's type and its number together.";
        }
        if (evidence.ShippingDocType is { } type)
        {
            if (answer.TransportMode is not { } mode)
            {
                return "A shipping document goes with a transport mode; answer the transport question too.";
            }
            if (!DocumentIds.Fits(type, mode))
            {
                return $"A {TrustAssessor.Label(type)} does not go with {Name(mode)} freight.";
            }
            if (DocumentIds.Problem(type, evidence.ShippingDocId!) is { } problem)
            {
                return problem;
            }
        }
        if (evidence.DispatchDate is { } dispatched)
        {
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            if (dispatched > today.AddDays(1))
            {
                return "The dispatch date is in the future.";
            }
            if (dispatched < invoice.IssueDate.AddDays(-366))
            {
                return "The dispatch date is more than a year before the invoice.";
            }
        }
        if ((evidence.EnergyCertType is null) == (evidence.EnergyCertId is not null))
        {
            return "Give the certificate's type and its id together.";
        }
        if (evidence.EnergyCertType is not null)
        {
            if (answer.EnergySource != EnergySource.Renewable)
            {
                return "A certificate goes with a renewable power answer.";
            }
            if (DocumentIds.CertificateProblem(evidence.EnergyCertId!) is { } problem)
            {
                return problem;
            }
        }
        return null;
    }

    private static string Name(TransportMode mode) => TrustAssessor.Name(mode);

    private static string Name(PlausibilityCode code) => code switch
    {
        PlausibilityCode.AirHeavy => "AIR_HEAVY",
        PlausibilityCode.SeaTooFast => "SEA_TOO_FAST",
        PlausibilityCode.MassBelowInvoice => "MASS_BELOW_INVOICE",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    private static string SignalName(TrustSignalCode code) => code switch
    {
        TrustSignalCode.SameNetworkAsLinkMaker => "SAME_NETWORK_AS_LINK_MAKER",
        TrustSignalCode.FastAnswer => "FAST_ANSWER",
        TrustSignalCode.SellerTaxIdInvalid => "SELLER_TAX_ID_INVALID",
        TrustSignalCode.ModeChangedOnLane => "MODE_CHANGED_ON_LANE",
        TrustSignalCode.PriorRejection => "PRIOR_REJECTION",
        TrustSignalCode.SimulatedAnswer => "SIMULATED_ANSWER",
        TrustSignalCode.ReceiptBeforeDispatch => "RECEIPT_BEFORE_DISPATCH",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    private static VerifyError Invalid() => new(VerifyErrorCode.InvalidToken, "This link is not valid.");

    private static VerifyError Used() => new(VerifyErrorCode.AlreadyUsed, "This link has already been used.");

    private static Result<VerifyOutcome, VerifyError> Fail(VerifyError error) => Result<VerifyOutcome, VerifyError>.Fail(error);
}
