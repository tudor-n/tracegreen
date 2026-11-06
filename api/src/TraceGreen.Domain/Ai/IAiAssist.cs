using TraceGreen.Domain.Ports;

namespace TraceGreen.Domain.Ai;

/// <summary>
/// One fact the chat model may cite (F13): its id, what it is about, and its value as the prompt shows it. The model
/// never writes the value; it writes <c>{{id}}</c> and the API fills in the value from the data.
/// </summary>
public sealed record ChatFactInput(string Id, string About, string Value);

/// <summary>The buyer's question, up to three earlier questions (never earlier answers) and the facts it may cite.</summary>
public sealed record ChatInput(string Question, IReadOnlyList<string> History, IReadOnlyList<ChatFactInput> Facts);

/// <summary>
/// The model's answer: a sentence with <c>{{id}}</c> placeholders and the ids it cites, or <see cref="Answerable"/>
/// false when the facts do not answer the question. <see cref="Provider"/> is the model id, or "stub".
/// </summary>
public sealed record ChatOutcome(bool Answerable, string Text, IReadOnlyList<string> FactIds, string Provider);

/// <summary>
/// The AI service's longer tasks, which no import waits on: the data chat (F13) and, later, PDF extraction (F9).
/// Implementations may throw on transport errors; callers fall back.
/// </summary>
public interface IAiAssist
{
    Task<ChatOutcome> ChatAsync(ChatInput input, CancellationToken ct);

    Task<HealthStatus> HealthAsync(CancellationToken ct);
}
