using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Domain.Services;

/// <summary>An alternative the AI picked for a node, by its id, with its reason (null when none survived the checks).</summary>
public sealed record SuggestionView(string AlternativeId, string? Rationale);

/// <summary>The AI's picks for a node and who made them: a model id, "stub", or "none" when the node has no alternatives.</summary>
public sealed record SuggestionsView(IReadOnlyList<SuggestionView> Suggestions, string Provider);

/// <summary>
/// AI-suggested alternatives (F11). The AI may only pick among the node's curated alternatives (the ones the simulator
/// already offers); any other id is dropped, the label stays the seed's, and a reason is kept only when it holds no digit
/// and passes the copy rules, so no figure and no supplier ever comes from a model. Asked on demand, under the same
/// timeout as classification, and never blocking: a failure means no suggestion. The simulator computes every number.
/// </summary>
public sealed class SuggestionService(IRepository repo, IAiClient ai, GraphService graph, ServiceOptions options)
{
    public async Task<SuggestionsView?> SuggestAsync(Ctx ctx, Guid nodeId, CancellationToken ct)
    {
        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        if (node is null)
        {
            return null;
        }
        var view = await graph.ViewAsync(ctx, node, ct);
        if (view.Alternatives.Count == 0)
        {
            return new SuggestionsView([], "none");
        }

        var input = new SuggestNodeInput(
            view.Classification.CategoryLabel, view.Classification.LineDescription, view.Country, view.Lane.Origin,
            Name(view.Lane.Mode), view.Status.ToString().ToUpperInvariant());
        var candidates = view.Alternatives
            .Select(a => new SuggestCandidateInput(a.Id, a.Label, a.Country, a.Origin, Name(a.Mode)))
            .ToList();

        SuggestOutcome outcome;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(options.AiTimeout);
            outcome = await ai.SuggestAsync(input, candidates, cts.Token).WaitAsync(options.AiTimeout, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new SuggestionsView([], "stub");
        }

        var allowed = view.Alternatives.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var picked = outcome.Suggestions
            .Where(s => allowed.Contains(s.AlternativeId))
            .DistinctBy(s => s.AlternativeId)
            .Select(s => new SuggestionView(s.AlternativeId, Reason(s.Rationale)))
            .ToList();
        return new SuggestionsView(picked, outcome.Provider);
    }

    /// <summary>
    /// Words that make a claim or a comparison nothing checks (emissions, cost, speed, distance): a reason holding one is
    /// dropped. The same list is in <c>ai/app/suggest.py</c>, which drops such a reason first.
    /// </summary>
    public static readonly IReadOnlySet<string> ClaimWords = new HashSet<string>(
        """
        carbon co2 co₂ emission emissions emit emits footprint greener green cleaner cheaper cheap
        cost costs price prices faster fast quicker slower distance distances km near nearer nearby
        close closer closest far farther further lower less more fewer higher better best worse
        reduce reduces reduced cut cuts saves save saving
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A model's reason as a reader may see it: no digit (so no figure), no claim word, one line, capped, within the copy
    /// rules; else null, and the pick shows without a reason.
    /// </summary>
    public static string? Reason(string? rationale)
    {
        if (rationale is null || rationale.Any(char.IsDigit))
        {
            return null;
        }
        var words = rationale.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', ',', ';', ':', '!', '?', '(', ')', '"', '\''));
        return words.Any(ClaimWords.Contains) ? null : NodeFactory.Reason(rationale);
    }

    private static string Name(TransportMode mode) => mode.ToString().ToLowerInvariant();
}
