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

/// <summary>
/// Port to the AI service. It classifies and suggests; it never returns a carbon number. Implementations may throw
/// on transport errors — callers fall back, they never let an AI failure block an import.
/// </summary>
public interface IAiClient
{
    string Name { get; }

    Task<IReadOnlyList<Classification>> ClassifyAsync(IReadOnlyList<ClassificationInput> lines, CancellationToken ct);

    Task<IReadOnlyList<Suggestion>> SuggestAsync(SupplierNode node, IReadOnlyList<Alternative> candidates, CancellationToken ct);

    Task<HealthStatus> HealthAsync(CancellationToken ct);
}
