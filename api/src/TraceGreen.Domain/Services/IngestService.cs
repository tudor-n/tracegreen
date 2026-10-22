using System.Text.Json.Nodes;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Security;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Services;

public enum IngestErrorCode { TooLarge, DoctypeForbidden, NotUblInvoice, Schema, Duplicate, UnsupportedCurrency, NoLane }

/// <summary><see cref="ExistingNodeId"/> is set on <see cref="IngestErrorCode.Duplicate"/>.</summary>
public sealed record IngestError(IngestErrorCode Code, string Message, Guid? ExistingNodeId = null);

/// <summary>Fixed ids and lane for the demo invoice, so every reset-and-import lands on the same node.</summary>
public sealed record IngestPins(Guid NodeId, Guid InvoiceId, string LaneId);

/// <summary>
/// Turns a UBL invoice into an ESTIMATED node: parse, refuse duplicates, classify (invoice code, cache, then the AI under
/// <see cref="ServiceOptions.AiTimeout"/>), estimate from spend, insert with an audit entry, then publish
/// <c>node.created</c> with fresh KPIs and <c>audit.appended</c>. The import never waits on the AI past the timeout.
/// </summary>
public sealed class IngestService(
    IRepository repo,
    IEventBus bus,
    IAiClient ai,
    SeedBundle seed,
    NodeViewMapper mapper,
    GraphService graph,
    TimeProvider clock,
    ServiceOptions options)
{
    /// <summary>Spend factors are per euro; other currencies wait for a source with FX conversion (D1).</summary>
    public const string SupportedCurrency = "EUR";

    /// <summary>Default lane by supplier country (decided at the scaffold). An unknown country is refused, never given an invented lane.</summary>
    public static readonly IReadOnlyDictionary<string, string> LaneByCountry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CN"] = "CN-RO",
        ["TW"] = "TW-RO",
        ["DE"] = "DE-HH-SB",
        ["RO"] = "RO-SB-SB",
    };

    /// <summary>
    /// Bounds on what one invoice may carry. They keep every later sum (KPIs over the graph, cost from mass × rate) far
    /// inside <see cref="decimal"/>'s range, so a hostile invoice cannot make the graph overflow for everyone.
    /// </summary>
    public const decimal MaxAmountEur = 1_000_000_000_000m;

    public const decimal MaxQuantity = 1_000_000_000m;

    private readonly UblInvoiceParser _parser = new();

    public async Task<Result<IngestResult, IngestError>> IngestAsync(Ctx ctx, string xml, IngestPins? pins, CancellationToken ct)
    {
        var parsed = _parser.Parse(xml);
        if (!parsed.IsOk)
        {
            return Fail(From(parsed.Error.Code), parsed.Error.Message);
        }
        var extracted = parsed.Value;
        if (OutOfRange(extracted) is { } problem)
        {
            return Fail(IngestErrorCode.Schema, problem);
        }

        // A cheap early answer before any AI wait; the repository repeats the check under its lock.
        if (await repo.GetInvoiceByRawHashAsync(ctx, Hashing.RawXmlHash(xml), ct) is { } existing)
        {
            var nodes = await repo.ListNodesAsync(ctx, ct);
            var existingNode = nodes.FirstOrDefault(n => n.InvoiceId == existing.Id);
            return Fail(IngestErrorCode.Duplicate, $"Invoice {existing.Extracted.Number} is already imported.", existingNode?.Id);
        }

        if (!string.Equals(extracted.Currency, SupportedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(IngestErrorCode.UnsupportedCurrency, $"Only {SupportedCurrency} invoices are estimated for now; this one is in {extracted.Currency}.");
        }

        var laneId = pins?.LaneId ?? LaneByCountry.GetValueOrDefault(extracted.Supplier.Country);
        var lane = seed.Lanes.FirstOrDefault(l => l.Id == laneId);
        if (lane is null)
        {
            var country = extracted.Supplier.Country.Length == 0 ? "(none)" : extracted.Supplier.Country;
            return Fail(IngestErrorCode.NoLane, $"No freight lane for supplier country {country}.");
        }

        var classifications = await LineClassification.ClassifyLinesAsync(extracted.Lines, ai, seed, options.AiTimeout, ct);
        var dominant = InvoiceFacts.DominantLine(extracted.Lines);
        var picked = classifications.First(c => c.LineId == dominant.Id);
        var classification = new NodeClassification(
            picked.CategoryId, picked.Source == ClassificationSource.Stub ? null : picked.Confidence, picked.Source);

        var now = clock.GetUtcNow();
        var invoice = NodeFactory.NewInvoice(pins?.InvoiceId ?? Ids.NewId(), ctx.WorkspaceId, extracted, xml, now);
        var node = NodeFactory.NewNode(
            pins?.NodeId ?? Ids.NewId(), invoice, classification, lane, NodeStatus.Estimated, null, null, seed, mapper.Factors, now);

        // Strings and integers only: the payload is stored as jsonb, which may re-render decimals and break the chain hash.
        var payload = new JsonObject
        {
            ["invoiceId"] = invoice.Id.ToString("D"),
            ["nodeId"] = node.Id.ToString("D"),
            ["number"] = extracted.Number,
            ["invoiceHash"] = invoice.InvoiceHash,
            ["categoryId"] = node.CategoryId,
            ["classificationSource"] = node.ClassificationSource.ToString().ToLowerInvariant(),
        };
        var inserted = await repo.InsertInvoiceWithNodeAsync(
            ctx, invoice, node, prev => AuditChain.Build(prev, ctx.WorkspaceId, now, ctx.Actor, "invoice.imported", payload), ct);
        if (inserted.Status == InsertInvoiceStatus.Duplicate || inserted.Entry is not { } entry)
        {
            return Fail(IngestErrorCode.Duplicate, $"Invoice {extracted.Number} is already imported.", inserted.ExistingNodeId);
        }

        var view = mapper.Map(node, invoice);
        var kpis = await graph.KpisAsync(ctx, ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new NodeCreated(seq, ts, ctx.WorkspaceId, view, kpis));
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new AuditAppended(seq, ts, ctx.WorkspaceId, entry));
        return Result<IngestResult, IngestError>.Ok(new IngestResult(view, kpis));
    }

    /// <summary>Why the invoice's numbers cannot be estimated safely, or null when they can.</summary>
    private static string? OutOfRange(ExtractedInvoice invoice)
    {
        if (invoice.Total <= 0m)
        {
            return $"The invoice total must be positive; it is {invoice.Total}.";
        }
        if (invoice.Total > MaxAmountEur)
        {
            return $"The invoice total exceeds {MaxAmountEur:N0}.";
        }
        foreach (var line in invoice.Lines)
        {
            if (Math.Abs(line.Amount) > MaxAmountEur)
            {
                return $"Line {line.Id}: the amount exceeds {MaxAmountEur:N0}.";
            }
            if (line.Quantity < 0m)
            {
                return $"Line {line.Id}: negative quantities (returns) are not supported yet.";
            }
            if (line.Quantity > MaxQuantity)
            {
                return $"Line {line.Id}: the quantity exceeds {MaxQuantity:N0}.";
            }
        }
        return null;
    }

    private static IngestErrorCode From(ParseErrorCode code) => code switch
    {
        ParseErrorCode.TooLarge => IngestErrorCode.TooLarge,
        ParseErrorCode.DoctypeForbidden => IngestErrorCode.DoctypeForbidden,
        ParseErrorCode.NotUblInvoice => IngestErrorCode.NotUblInvoice,
        _ => IngestErrorCode.Schema,
    };

    private static Result<IngestResult, IngestError> Fail(IngestErrorCode code, string message, Guid? existingNodeId = null) =>
        Result<IngestResult, IngestError>.Fail(new IngestError(code, message, existingNodeId));
}
