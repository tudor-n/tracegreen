using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Domain.Ai;

public sealed record ClassificationInput(
    string LineId,
    string Description,
    decimal? Quantity,
    string? UnitCode,
    string? ClassificationCode,
    string? ClassificationListId);

public sealed record Classification(string LineId, string CategoryId, double Confidence, string Rationale, ClassificationSource Source);

public sealed record Suggestion(string AlternativeId, string Rationale, string Label);

/// <summary>What the AI is told about the node for a suggestion (F11): what it buys and how it ships, never a figure.</summary>
public sealed record SuggestNodeInput(string CategoryLabel, string LineDescription, string Country, string Origin, string Mode, string Status);

/// <summary>A curated alternative the AI may pick (seed/alternatives.json), never a figure.</summary>
public sealed record SuggestCandidateInput(string AlternativeId, string Label, string Country, string Origin, string Mode);

/// <summary>The AI's picks and who made them (a model id, or "stub").</summary>
public sealed record SuggestOutcome(IReadOnlyList<Suggestion> Suggestions, string Provider);

/// <summary>
/// Port to the AI service. It classifies and suggests; it never returns a carbon number. Implementations may throw
/// on transport errors — callers fall back, they never let an AI failure block an import.
/// </summary>
public interface IAiClient
{
    string Name { get; }

    Task<IReadOnlyList<Classification>> ClassifyAsync(IReadOnlyList<ClassificationInput> lines, CancellationToken ct);

    /// <summary>Picks among <paramref name="candidates"/> only; the caller still checks every id it gets back.</summary>
    Task<SuggestOutcome> SuggestAsync(SuggestNodeInput node, IReadOnlyList<SuggestCandidateInput> candidates, CancellationToken ct);

    Task<HealthStatus> HealthAsync(CancellationToken ct);
}
