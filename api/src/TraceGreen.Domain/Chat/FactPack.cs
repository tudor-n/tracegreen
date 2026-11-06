using System.Globalization;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Chat;

/// <summary>How the SPA formats a fact's <see cref="Fact.Value"/>: kg as tonnes, euros, days, a share, a count, a date, or text.</summary>
public enum FactKind { CarbonKg, Eur, Days, Share, Count, Date, Text, Status, Mode }

/// <summary>
/// One value the chat may cite (F13). <see cref="Value"/> is the raw value in invariant form (kg, euros, a 0–1 share, an
/// ISO date, a status or mode name, or text), which the SPA formats by <see cref="Kind"/>; <see cref="Field"/> says what
/// it is (the SPA's label), <see cref="Subject"/> whose it is (a supplier's name, or null for a total). The model sees
/// <see cref="About"/> and <see cref="PromptText"/> only, and cites the fact as <c>{{Id}}</c>; it never writes the value.
/// </summary>
public sealed record Fact(
    string Id,
    string Field,
    FactKind Kind,
    string Value,
    string About,
    string PromptText,
    string? Subject,
    Guid? NodeId,
    long? AuditId);

/// <summary>
/// A change the simulator worked out for the chat (F13): the node, the change (a transport mode or an alternative
/// supplier) and its result, the same as the What if tab shows. Nothing is saved.
/// </summary>
public sealed record WhatIf(NodeView Node, ScenarioChange Change, ScenarioResult Result);

/// <summary>
/// The facts of one chat question, built from the dashboard snapshot. Every figure is the API's own (the node views,
/// the KPIs, the saved scenarios), and every comparison the model might need is computed here: suppliers are numbered
/// by emissions (S1 is the largest), and totals, counts by status and the supplier-confirmed share are facts of their
/// own, so the model never calculates. <see cref="Names"/> lists the names the answer may only cite, never write out.
/// </summary>
public sealed class FactPack
{
    public const int MaxSuppliers = 40;
    public const int MaxScenarios = 10;
    public const int MaxAuditEntries = 10;
    public const int MaxWhatIfs = 12;

    /// <summary>The largest suppliers the simulator works out what-ifs for (<c>ChatService</c>).</summary>
    public const int WhatIfSuppliers = 6;

    private readonly Dictionary<string, Fact> _byId;

    private FactPack(IReadOnlyList<Fact> facts, IReadOnlyList<string> names)
    {
        Facts = facts;
        Names = names;
        _byId = facts.ToDictionary(f => f.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<Fact> Facts { get; }

    /// <summary>Supplier, buyer, alternative and lane-origin names: an answer that writes one out instead of citing it fails.</summary>
    public IReadOnlyList<string> Names { get; }

    public Fact? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>How many suppliers are numbered S1…Sn.</summary>
    public int SupplierCount => Facts.Count(f => f.Field == "name" && f.Id.StartsWith('S'));

    /// <summary>The rank n of a supplier fact <c>Sn.*</c>, or null for any other fact.</summary>
    public static int? RankOf(string factId)
    {
        if (factId.Length < 2 || factId[0] != 'S')
        {
            return null;
        }
        var dot = factId.IndexOf('.');
        return dot > 1 && int.TryParse(factId.AsSpan(1, dot - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>
    /// The transport modes a supplier's what-ifs offer ("rail" from "switch to rail"), by supplier name, for the suppliers
    /// the simulator looked at; a supplier with an <c>Sn.whatIf</c> fact is there with none.
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>> ModesOnOffer => _modesOnOffer ??= OffersOf(Facts);

    private IReadOnlyDictionary<string, HashSet<string>>? _modesOnOffer;

    private static Dictionary<string, HashSet<string>> OffersOf(IEnumerable<Fact> facts)
    {
        var offers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in facts.Where(f => f.Subject is not null && (f.Field == "whatIf" || (f.Field == "change" && f.Id[0] == 'W'))))
        {
            var modes = offers.TryGetValue(fact.Subject!, out var known) ? known : offers[fact.Subject!] = new(StringComparer.Ordinal);
            if (fact.Field == "change" && fact.Value.StartsWith("switch to ", StringComparison.Ordinal))
            {
                modes.Add(fact.Value["switch to ".Length..]);
            }
        }
        return offers;
    }

    /// <summary>The status (VERIFIED, PARTIAL, ESTIMATED) of supplier <c>Sn</c>.</summary>
    public string? StatusOf(int rank) => Find($"S{rank}.status")?.Value;

    /// <summary>
    /// <paramref name="whatIfs"/> are the simulator's results for the largest suppliers (<c>ChatService</c>); they become
    /// the W facts, W1 the largest cut in emissions, so the model can say what lowers emissions or cost without guessing.
    /// </summary>
    public static FactPack Build(GraphSnapshot snapshot, IReadOnlyList<Scenario> scenarios, IReadOnlyList<WhatIf>? whatIfs = null)
    {
        var facts = new List<Fact>();
        var names = new List<string> { snapshot.Buyer.Name };
        var ranked = snapshot.Nodes.OrderByDescending(n => n.CarbonKg).ThenBy(n => n.Label, StringComparer.Ordinal).ToList();
        var shown = ranked.Take(MaxSuppliers).ToList();
        var kpis = snapshot.Kpis;

        void Add(string id, string field, FactKind kind, string value, string about, string prompt, string? subject = null, Guid? node = null, long? audit = null) =>
            facts.Add(new Fact(id, field, kind, value, about, prompt, subject, node, audit));

        Add("total.carbon", "totalCarbon", FactKind.CarbonKg, Raw(kpis.TotalKg), "total emissions of all deliveries", Tonnes(kpis.TotalKg));
        Add("total.spend", "totalSpend", FactKind.Eur, Raw(kpis.SpendEur), "total spend of all deliveries", Euros(kpis.SpendEur));
        Add("count.deliveries", "deliveries", FactKind.Count, Raw(kpis.NodeCount), "number of deliveries (one per invoice)", Count(kpis.NodeCount));
        Add("count.suppliers", "suppliers", FactKind.Count, Raw(snapshot.Nodes.Select(n => n.Label).Distinct(StringComparer.Ordinal).Count()),
            "number of distinct suppliers", Count(snapshot.Nodes.Select(n => n.Label).Distinct(StringComparer.Ordinal).Count()));
        foreach (var status in new[] { NodeStatus.Verified, NodeStatus.Partial, NodeStatus.Estimated })
        {
            var n = snapshot.Nodes.Count(x => x.Status == status);
            Add($"count.{StatusKey(status)}", StatusKey(status), FactKind.Count, Raw(n), $"number of deliveries that are {StatusWords(status)}", Count(n));
        }
        Add("share.confirmed", "confirmedShare", FactKind.Share, Raw((decimal)kpis.PrimaryShare),
            "supplier-confirmed share of total emissions (by emissions, not by node count)", Percent(kpis.PrimaryShare));
        // The sets a "which suppliers are …" question asks for, so the model cites one fact instead of picking names.
        foreach (var status in new[] { NodeStatus.Verified, NodeStatus.Partial, NodeStatus.Estimated })
        {
            var labels = ranked.Where(n => n.Status == status).Select(n => n.Label).Distinct(StringComparer.Ordinal).ToList();
            var list = labels.Count == 0 ? "none" : string.Join(", ", labels);
            Add($"list.{StatusKey(status)}", "list" + char.ToUpperInvariant(StatusKey(status)[0]) + StatusKey(status)[1..], FactKind.Text, list,
                $"the suppliers whose deliveries are {StatusWords(status)}, largest first", list);
        }

        // One S per delivery: a second invoice from the same supplier says so, so an answer can say "two deliveries"
        // instead of naming the supplier twice as if they were two.
        var firstRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < shown.Count; i++)
        {
            var node = shown[i];
            var id = $"S{i + 1}";
            var rank = i == 0 ? "the largest emitter" : $"rank {i + 1} by emissions";
            var again = firstRank.TryGetValue(node.Label, out var first) ? $", another delivery from the same supplier as S{first}" : "";
            firstRank.TryAdd(node.Label, i + 1);
            var about = $"supplier {id} ({rank}{again})";
            var mode = node.TransportMode ?? node.Lane.Mode;
            names.Add(node.Label);
            names.Add(node.Lane.Origin);
            Add($"{id}.name", "name", FactKind.Text, node.Label, $"{about}: name", node.Label, node.Label, node.Id);
            Add($"{id}.carbon", "carbon", FactKind.CarbonKg, Raw(node.CarbonKg), $"{about}: emissions", Tonnes(node.CarbonKg), node.Label, node.Id);
            Add($"{id}.status", "status", FactKind.Status, StatusName(node.Status), $"{about}: status", StatusWords(node.Status), node.Label, node.Id);
            Add($"{id}.basis", "basis", FactKind.Text, Provenance(node), $"{about}: where the emissions figure comes from", Provenance(node), node.Label, node.Id);
            Add($"{id}.category", "category", FactKind.Text, node.Classification.CategoryLabel, $"{about}: what it supplies", node.Classification.CategoryLabel, node.Label, node.Id);
            Add($"{id}.country", "country", FactKind.Text, node.Country, $"{about}: country", node.Country, node.Label, node.Id);
            Add($"{id}.origin", "origin", FactKind.Text, node.Lane.Origin, $"{about}: ships from", node.Lane.Origin, node.Label, node.Id);
            Add($"{id}.mode", "mode", FactKind.Mode, ModeName(mode), $"{about}: transport mode", ModeName(mode), node.Label, node.Id);
            Add($"{id}.spend", "spend", FactKind.Eur, Raw(node.SpendEur), $"{about}: spend on the invoice", Euros(node.SpendEur), node.Label, node.Id);
            Add($"{id}.cost", "cost", FactKind.Eur, Raw(node.CostEur), $"{about}: cost with freight (illustrative)", Euros(node.CostEur), node.Label, node.Id);
            Add($"{id}.leadTime", "leadTime", FactKind.Days, Raw(node.LeadDays), $"{about}: lead time (illustrative)", Days(node.LeadDays), node.Label, node.Id);
            Add($"{id}.invoice", "invoice", FactKind.Text, node.Invoice.Number, $"{about}: invoice number", node.Invoice.Number, node.Label, node.Id);
            Add($"{id}.issued", "issued", FactKind.Date, Raw(node.Invoice.IssueDate), $"{about}: invoice date", Raw(node.Invoice.IssueDate), node.Label, node.Id);
            names.AddRange(node.Alternatives.Select(a => a.Label));
        }
        if (ranked.Count > shown.Count)
        {
            var rest = ranked.Skip(shown.Count).ToList();
            var restKg = rest.Sum(n => n.CarbonKg);
            Add("others.count", "othersCount", FactKind.Count, Raw(rest.Count), "number of further deliveries not listed one by one", Count(rest.Count));
            Add("others.carbon", "othersCarbon", FactKind.CarbonKg, Raw(restKg), "emissions of the further deliveries together", Tonnes(restKg));
        }

        var byNode = snapshot.Nodes.ToDictionary(n => n.Id);
        var saved = scenarios.OrderByDescending(s => s.CreatedAt).Take(MaxScenarios).ToList();
        Add("count.scenarios", "scenarios", FactKind.Count, Raw(scenarios.Count), "number of saved what-if scenarios", Count(scenarios.Count));
        for (var i = 0; i < saved.Count; i++)
        {
            var scenario = saved[i];
            if (!byNode.TryGetValue(scenario.NodeId, out var node))
            {
                continue;
            }
            var id = $"C{i + 1}";
            var about = $"saved scenario {id}";
            var change = ChangeLabel(node, scenario.Change);
            Add($"{id}.supplier", "name", FactKind.Text, node.Label, $"{about}: the supplier it changes", node.Label, node.Label, node.Id);
            Add($"{id}.change", "change", FactKind.Text, change, $"{about}: the change", change, node.Label, node.Id);
            Add($"{id}.carbonDelta", "carbonDelta", FactKind.CarbonKg, Raw(scenario.Result.Delta.CarbonKg), $"{about}: change in emissions (negative is less)", SignedTonnes(scenario.Result.Delta.CarbonKg), node.Label, node.Id);
            Add($"{id}.costDelta", "costDelta", FactKind.Eur, Raw(scenario.Result.Delta.CostEur), $"{about}: change in cost (illustrative; negative is less)", SignedEuros(scenario.Result.Delta.CostEur), node.Label, node.Id);
        }

        // What the simulator says each change would do for the largest suppliers, biggest emissions cut first. The
        // figures are the API's (Carbon/Simulator); cost and lead time are illustrative, as in the What if tab.
        var options = (whatIfs ?? [])
            .Where(w => !w.Result.MassUnknown)
            .OrderBy(w => w.Result.Delta.CarbonKg)
            .ThenBy(w => w.Result.Delta.CostEur)
            .Take(MaxWhatIfs)
            .ToList();
        Add("count.whatIfs", "whatIfs", FactKind.Count, Raw(options.Count), "number of what-ifs the simulator worked out for the largest suppliers (nothing saved)", Count(options.Count));
        for (var i = 0; i < options.Count; i++)
        {
            var (node, change, result) = options[i];
            var id = $"W{i + 1}";
            var about = i == 0 ? $"what-if {id} (of all the what-ifs, the largest cut in emissions)" : $"what-if {id}";
            var label = ChangeLabel(node, change);
            Add($"{id}.supplier", "name", FactKind.Text, node.Label, $"{about}: the supplier it would change", node.Label, node.Label, node.Id);
            Add($"{id}.change", "change", FactKind.Text, label, $"{about}: the change", label, node.Label, node.Id);
            Add($"{id}.carbonDelta", "carbonDelta", FactKind.CarbonKg, Raw(result.Delta.CarbonKg), $"{about}: change in emissions", SignedTonnes(result.Delta.CarbonKg), node.Label, node.Id);
            Add($"{id}.costDelta", "costDelta", FactKind.Eur, Raw(result.Delta.CostEur), $"{about}: change in cost with freight (illustrative)", SignedEuros(result.Delta.CostEur), node.Label, node.Id);
            Add($"{id}.leadDelta", "leadDelta", FactKind.Days, Raw(result.Delta.LeadDays), $"{about}: change in lead time (illustrative)", SignedDays(result.Delta.LeadDays), node.Label, node.Id);
            Add($"{id}.badge", "badge", FactKind.Text, BadgeName(result.Badge), $"{about}: the simulator's verdict", BadgeWords(result.Badge), node.Label, node.Id);
        }

        // A large supplier the simulator found no what-if for says so, so an answer says why instead of inventing one.
        if (whatIfs is not null)
        {
            var simulated = whatIfs.Where(w => !w.Result.MassUnknown).Select(w => w.Node.Id).ToHashSet();
            for (var i = 0; i < Math.Min(WhatIfSuppliers, shown.Count); i++)
            {
                var node = shown[i];
                if (simulated.Contains(node.Id))
                {
                    continue;
                }
                var why = node.MassT is null
                    ? "none: the invoice gives no mass, so a change of transport cannot be estimated"
                    : "none: its lane has no other transport mode and it has no alternative supplier";
                Add($"S{i + 1}.whatIf", "whatIf", FactKind.Text, "none", $"supplier S{i + 1}: what-ifs in What if", why, node.Label, node.Id);
            }
        }

        var recent = snapshot.Audit.OrderByDescending(a => a.Id).Take(MaxAuditEntries).ToList();
        Add("count.auditEntries", "auditEntries", FactKind.Count, Raw(snapshot.Audit.Count), "number of entries in the audit log", Count(snapshot.Audit.Count));
        for (var i = 0; i < recent.Count; i++)
        {
            var entry = recent[i];
            var id = $"A{i + 1}";
            var about = i == 0 ? "the latest audit log entry" : $"audit log entry {id} ({i + 1} from the latest)";
            Add($"{id}.action", "action", FactKind.Text, entry.Action, $"{about}: what happened", entry.Action, null, null, entry.Id);
            Add($"{id}.when", "when", FactKind.Date, Raw(DateOnly.FromDateTime(entry.Ts.UtcDateTime)), $"{about}: date", Raw(DateOnly.FromDateTime(entry.Ts.UtcDateTime)), null, null, entry.Id);
        }

        var cleanNames = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new FactPack(facts, cleanNames);
    }

    /// <summary>
    /// Ruling 3's provenance line, the same words the side panel shows (web/src/lib/node-display.ts, provenanceLine):
    /// "Supplier-confirmed · source year" with any further sources after it, or "Spend-based estimate · source".
    /// </summary>
    private static string Provenance(NodeView node)
    {
        if (node.Basis == CarbonBasis.Activity && node.Activity is { } activity)
        {
            var rows = activity.Factors.Select(f => $"{f.Source} {f.Year}").Distinct(StringComparer.Ordinal).ToList();
            return rows.Count == 0 ? "Supplier-confirmed" : string.Join("; ", ["Supplier-confirmed · " + rows[0], .. rows.Skip(1)]);
        }
        var spend = node.Spend.Factors.FirstOrDefault();
        return spend is null ? "Spend-based estimate" : $"Spend-based estimate · {spend.Source}";
    }

    private static string StatusKey(NodeStatus status) => status switch
    {
        NodeStatus.Verified => "confirmed",
        NodeStatus.Partial => "partial",
        _ => "estimated",
    };

    private static string StatusWords(NodeStatus status) => status switch
    {
        NodeStatus.Verified => "supplier-confirmed",
        NodeStatus.Partial => "partly confirmed",
        _ => "estimated",
    };

    private static string StatusName(NodeStatus status) => status.ToString().ToUpperInvariant();

    private static string ModeName(TransportMode mode) => mode.ToString().ToLowerInvariant();

    private static string ChangeLabel(NodeView node, ScenarioChange change) => change.Mode is { } m
        ? $"switch to {ModeName(m)}"
        : $"switch to {node.Alternatives.FirstOrDefault(a => a.Id == change.AlternativeId)?.Label ?? "another supplier"}";

    /// <summary>The badge as the API's JSON names it, which the SPA reads.</summary>
    private static string BadgeName(MacBadge badge) => badge switch
    {
        MacBadge.WinWin => "win-win",
        MacBadge.BelowEts => "below-ets",
        MacBadge.Premium => "premium",
        _ => "not-abatement",
    };

    private static string BadgeWords(MacBadge badge) => badge switch
    {
        MacBadge.WinWin => "win-win: less carbon and less cost",
        MacBadge.BelowEts => "less carbon, at a cost per tonne below the EU carbon price",
        MacBadge.Premium => "less carbon, at a cost per tonne above the EU carbon price",
        _ => "not an abatement option: it saves no carbon",
    };

    // A change reads with its sign and a word, so a model cannot take an increase for a cut.
    private static string Direction(decimal value) => value < 0 ? " (less)" : value > 0 ? " (more)" : " (no change)";

    private static string SignedTonnes(decimal kg) =>
        (kg / 1000m).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " t CO2e" + Direction(kg);

    private static string SignedEuros(decimal eur) =>
        (eur < 0 ? "-" : eur > 0 ? "+" : "") + "EUR " + Math.Round(Math.Abs(eur), 0).ToString("#,0", CultureInfo.InvariantCulture) + Direction(eur);

    private static string SignedDays(int days) =>
        days.ToString("+0;-0;0", CultureInfo.InvariantCulture) + (Math.Abs(days) == 1 ? " day" : " days") + (days < 0 ? " (faster)" : days > 0 ? " (slower)" : " (no change)");

    private static string Raw(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Raw(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Raw(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The prompt's own renderings, for the model to read and compare; the SPA formats what the reader sees.
    private static string Tonnes(decimal kg) => (kg / 1000m).ToString("0.0", CultureInfo.InvariantCulture) + " t CO2e";

    private static string Euros(decimal eur) => "EUR " + Math.Round(eur, 0).ToString("#,0", CultureInfo.InvariantCulture);

    private static string Days(int days) => days.ToString(CultureInfo.InvariantCulture) + (days == 1 ? " day" : " days");

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Percent(double share) => Math.Round(share * 100).ToString(CultureInfo.InvariantCulture) + "%";
}
