using System.Text.Json.Nodes;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Services;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Seed;

public sealed record DemoStateData(
    IReadOnlyList<Invoice> Invoices,
    IReadOnlyList<SupplierNode> Nodes,
    IReadOnlyList<Verification> Verifications,
    IReadOnlyList<NewAuditEntry> Audit);

/// <summary>
/// Builds the demo start state from the seed bundle: the present nodes with their estimates, the seeded verifications
/// and a valid audit chain. Deterministic, so both repositories load exactly the same rows.
/// </summary>
public static class DemoState
{
    public static readonly DateTimeOffset SeedTime = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    public static DemoStateData Build(SeedBundle bundle)
    {
        var parser = new UblInvoiceParser();
        var table = FactorTable.FromSeed(bundle);
        var lanes = bundle.Lanes.ToDictionary(l => l.Id);
        var workspaceId = bundle.DemoState.WorkspaceId;

        var invoices = new List<Invoice>();
        var nodes = new List<SupplierNode>();
        var verifications = new List<Verification>();
        var audit = new List<NewAuditEntry>();
        AuditEntry? prev = null;

        void Append(DateTimeOffset at, string action, JsonNode payload)
        {
            var entry = AuditChain.Build(prev, workspaceId, at, "seed", action, payload);
            audit.Add(entry);
            prev = AuditChain.WithId(entry, audit.Count);
        }

        var index = 0;
        foreach (var spec in bundle.DemoState.Nodes.Where(n => n.Present))
        {
            var createdAt = SeedTime.AddMinutes(index++);
            var xml = File.ReadAllText(bundle.FilePath(spec.InvoiceFile));
            var parsed = parser.Parse(xml);
            if (!parsed.IsOk)
            {
                throw new SeedValidationException([$"{spec.InvoiceFile}: {parsed.Error.Message}"]);
            }
            var extracted = parsed.Value;
            var (invoice, node) = Materialize(spec, extracted, xml, bundle, table, lanes[spec.LaneId], createdAt);
            invoices.Add(invoice);
            nodes.Add(node);
        }

        Append(SeedTime, "seed.loaded", new JsonObject { ["nodes"] = nodes.Count });

        foreach (var spec in bundle.DemoState.Nodes.Where(n => n.Present && n.VerificationId is not null))
        {
            var node = nodes.Single(n => n.Id == spec.NodeId);
            var at = node.CreatedAt.AddHours(1);
            verifications.Add(new Verification(
                spec.VerificationId!.Value, workspaceId, spec.NodeId, spec.Nonce!, spec.TransportMode, spec.EnergySource,
                node.MassT, at, "seed", at));
            Append(at, "verification.submitted", new JsonObject
            {
                ["nodeId"] = spec.NodeId.ToString("D"),
                ["nonce"] = spec.Nonce,
                ["transportMode"] = spec.TransportMode?.ToString().ToLowerInvariant(),
                ["energySource"] = spec.EnergySource?.ToString().ToLowerInvariant(),
            });
        }

        return new DemoStateData(invoices, nodes, verifications, audit);
    }

    private static (Invoice Invoice, SupplierNode Node) Materialize(
        DemoNodeSpec spec, ExtractedInvoice extracted, string xml, SeedBundle bundle, FactorTable table, Lane lane, DateTimeOffset createdAt)
    {
        var invoice = NodeFactory.NewInvoice(spec.InvoiceId, bundle.DemoState.WorkspaceId, extracted, xml, createdAt);
        var (categoryId, confidence, source, _) = InvoiceClassification.Offline(InvoiceFacts.DominantLine(extracted.Lines), bundle);
        var node = NodeFactory.NewNode(
            spec.NodeId, invoice, new NodeClassification(categoryId, confidence, source), lane, spec.Status,
            spec.TransportMode, spec.EnergySource, bundle, table, createdAt);
        return (invoice, node);
    }
}
