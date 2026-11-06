using System.Diagnostics;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Chat;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Domain.Services;

/// <summary>How a chat question ended: answered from the data, not answerable from it, or no AI to ask.</summary>
public enum ChatStatus { Answered, NotAnswered, Unavailable }

/// <summary>A fact an answer cites, as the SPA shows it: the raw value with its kind, what it is, whose it is and where it links.</summary>
public sealed record ChatFactView(string Id, string Field, FactKind Kind, string Value, string? Subject, Guid? NodeId, long? AuditId);

/// <summary>
/// A chat answer (F13). <see cref="Segments"/> is the sentence: model-written text and fact ids in turn; every fact id is
/// in <see cref="Facts"/>, with the value from the data. Empty unless <see cref="Status"/> is Answered.
/// </summary>
public sealed record ChatAnswer(ChatStatus Status, IReadOnlyList<AnswerSegment> Segments, IReadOnlyList<ChatFactView> Facts, string Provider);

/// <summary>
/// The answer and, for the host's log only, why it was not answered (a rule of <see cref="AnswerCheck"/>, or the AI's
/// failure); never the model's text, which may hold invoice names.
/// </summary>
public sealed record ChatResult(ChatAnswer Answer, string? LogNote);

/// <summary>
/// A model the chat box offers: the id to send back with a question, its provider and name, whether it can answer now,
/// and whether "Automatic" asks it (a local Ollama model the server found is asked only when picked).
/// </summary>
public sealed record ChatModelView(string Id, string Provider, string Model, bool Ready, string? Reason, bool Automatic);

/// <summary>
/// The chat box's model choice: the models "Automatic" asks, in its order, then the local ones. <see cref="Available"/>
/// is false when the AI service did not answer; an empty list with it true means no model is configured or found.
/// </summary>
public sealed record ChatModels(bool Available, IReadOnlyList<ChatModelView> Models);

/// <summary>Maps to HTTP: <see cref="InvalidQuestion"/>, <see cref="UnknownModel"/> and <see cref="ModelNotReady"/> 422.</summary>
public enum ChatErrorCode { InvalidQuestion, UnknownModel, ModelNotReady }

public sealed record ChatError(ChatErrorCode Code, string Message);

/// <summary>
/// The data chat (F13): "the model writes, the app checks". The facts come from the dashboard snapshot (<see cref="FactPack"/>);
/// the model writes the sentence but cites every value and name by fact id; <see cref="AnswerCheck"/> refuses anything
/// else, and the values the reader sees are filled in here from the data. A refused or unanswerable question says so; it
/// is never answered with a guess. Read-only: nothing is stored or audited, and the model's text is never logged.
/// </summary>
public sealed class ChatService(IRepository repo, GraphService graph, IAiAssist ai, ScenarioService simulator, ServiceOptions options)
{

    public const int MaxQuestionChars = 300;
    public const int MaxHistory = 3;
    public const int MaxModelIdChars = 120;

    /// <summary>Below this much of the chat's time left, an answer the check refused is not asked for again.</summary>
    public static readonly TimeSpan MinRetryTime = TimeSpan.FromSeconds(3);

    /// <summary>The model list is a quick read; past this the chat box offers "Automatic" only.</summary>
    public static readonly TimeSpan ModelsTimeout = TimeSpan.FromSeconds(3);

    private static readonly ChatError UnknownModel = new(ChatErrorCode.UnknownModel, "The chat does not offer that model.");

    /// <summary>The models the chat box offers; never fails, since the chat works without a choice.</summary>
    public async Task<ChatModels> ModelsAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ModelsTimeout);
            var models = await ai.ChatModelsAsync(cts.Token).WaitAsync(ModelsTimeout, ct);
            return new ChatModels(true, models.Select(m => new ChatModelView(m.Id, m.Provider, m.Model, m.Ready, m.Reason, m.Automatic)).ToList());
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new ChatModels(false, []);
        }
    }

    /// <summary>
    /// <paramref name="model"/> is one of <see cref="ModelsAsync"/>'s ids, which then answers alone, or null for the
    /// chat's models in order.
    /// </summary>
    public async Task<Result<ChatResult, ChatError>> AskAsync(
        Ctx ctx, string? question, IReadOnlyList<string>? history, string? model, CancellationToken ct)
    {
        var asked = question?.Trim() ?? "";
        if (asked.Length == 0 || asked.Length > MaxQuestionChars)
        {
            return Result<ChatResult, ChatError>.Fail(new ChatError(
                ChatErrorCode.InvalidQuestion, $"Ask a question of 1 to {MaxQuestionChars} characters."));
        }
        var chosen = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (chosen is { Length: > MaxModelIdChars })
        {
            return Result<ChatResult, ChatError>.Fail(UnknownModel);
        }
        var earlier = (history ?? [])
            .Select(h => h?.Trim() ?? "")
            .Where(h => h.Length > 0)
            .Select(h => h.Length > MaxQuestionChars ? h[..MaxQuestionChars] : h)
            .TakeLast(MaxHistory)
            .ToList();

        var snapshot = await graph.SnapshotAsync(ctx, ct);
        var scenarios = await repo.ListScenariosAsync(ctx, null, ct);
        var pack = FactPack.Build(snapshot, scenarios, await WhatIfsAsync(ctx, snapshot, ct));
        var input = new ChatInput(asked, earlier, pack.Facts.Select(f => new ChatFactInput(f.Id, f.About, f.PromptText)).ToList(), chosen);

        var started = Stopwatch.GetTimestamp();
        ChatOutcome outcome;
        try
        {
            outcome = await AskAi(input, options.AiChatTimeout, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Ok(ChatStatus.Unavailable, "stub", $"the AI service did not answer ({ex.GetType().Name})");
        }

        switch (outcome.ModelProblem)
        {
            case ChatModelProblem.Unknown:
                return Result<ChatResult, ChatError>.Fail(UnknownModel);
            case ChatModelProblem.NotReady:
                return Result<ChatResult, ChatError>.Fail(new ChatError(
                    ChatErrorCode.ModelNotReady, "That model is not set up on the server yet; pick another one or Automatic."));
        }
        if (!outcome.Answerable)
        {
            return Ok(outcome.Provider == "stub" ? ChatStatus.Unavailable : ChatStatus.NotAnswered, outcome.Provider, null);
        }
        var checkedAnswer = AnswerCheck.Check(outcome.Text, pack);
        string? note = null;
        if (!checkedAnswer.Ok)
        {
            // The same model is asked once more, told the rule its answer broke, while the chat has time left; failing
            // that, a long answer is shown without its one false sentence (AnswerCheck.Trim). The log gets the rules,
            // never the text: it may hold invoice names and is the model's, not the buyer's.
            note = $"answer from {outcome.Provider} refused: {checkedAnswer.Problem}";
            var left = options.AiChatTimeout - Stopwatch.GetElapsedTime(started);
            ChatOutcome? again = null;
            if (left >= MinRetryTime)
            {
                var retry = new ChatRetry(outcome.Text, AnswerCheck.RetryHint(checkedAnswer.Problem!));
                try
                {
                    again = await AskAi(input with { Model = outcome.Provider, Retry = retry }, left, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    note += $"; asked again, no answer ({ex.GetType().Name})";
                }
            }
            var second = again is { Answerable: true, ModelProblem: null } ? AnswerCheck.Check(again.Text, pack) : null;
            if (second is { Ok: true })
            {
                (outcome, checkedAnswer, note) = (again!, second, null);
            }
            else
            {
                if (again is not null)
                {
                    note += $"; asked again: {second?.Problem ?? "no answer"}";
                }
                // The retried answer trimmed if that passes, else the first one trimmed.
                var (trimmed, from) = second is not null && AnswerCheck.Trim(again!.Text, pack) is { } shorter
                    ? (shorter, again)
                    : (AnswerCheck.Trim(outcome.Text, pack), outcome);
                if (trimmed is null)
                {
                    return Ok(ChatStatus.NotAnswered, outcome.Provider, note);
                }
                (outcome, checkedAnswer, note) = (from, trimmed, $"{note}; shown without the sentence that broke the rule");
            }
        }

        var facts = checkedAnswer.FactIds
            .Select(id => pack.Find(id)!)
            .Select(f => new ChatFactView(f.Id, f.Field, f.Kind, f.Value, f.Subject, f.NodeId, f.AuditId))
            .ToList();
        return Result<ChatResult, ChatError>.Ok(new ChatResult(new ChatAnswer(ChatStatus.Answered, checkedAnswer.Segments, facts, outcome.Provider), note));
    }

    /// <summary>
    /// The What if tab's results for the largest suppliers, worked out by the same simulator and never saved: each other
    /// mode the lane has and each alternative. A change the simulator refuses (no mass, no such leg) is left out.
    /// </summary>
    private async Task<IReadOnlyList<WhatIf>> WhatIfsAsync(Ctx ctx, GraphSnapshot snapshot, CancellationToken ct)
    {
        var found = new List<WhatIf>();
        foreach (var node in snapshot.Nodes.OrderByDescending(n => n.CarbonKg).ThenBy(n => n.Label, StringComparer.Ordinal).Take(FactPack.WhatIfSuppliers))
        {
            var current = node.TransportMode ?? node.Lane.Mode;
            var changes = node.Lane.Modes.Where(m => m != current).Select(m => new ScenarioChange(m, null))
                .Concat(node.Alternatives.Select(a => new ScenarioChange(null, a.Id)));
            foreach (var change in changes)
            {
                var run = await simulator.SimulateAsync(ctx, node.Id, change, ct);
                if (run.IsOk)
                {
                    found.Add(new WhatIf(node, change, run.Value));
                }
            }
        }
        return found;
    }

    private async Task<ChatOutcome> AskAi(ChatInput input, TimeSpan budget, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        return await ai.ChatAsync(input, cts.Token).WaitAsync(budget, ct);
    }

    private static Result<ChatResult, ChatError> Ok(ChatStatus status, string provider, string? note) =>
        Result<ChatResult, ChatError>.Ok(new ChatResult(new ChatAnswer(status, [], [], provider), note));
}
