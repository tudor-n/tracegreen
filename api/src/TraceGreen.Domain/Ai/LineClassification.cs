using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Ai;

/// <summary>
/// Classifies invoice lines in this order: the invoice's own classification code, the pre-computed cache, then the AI
/// service under a hard timeout. Whatever the AI answers is checked against the allow-list; whatever it fails to answer
/// becomes UNCLASSIFIED. This never throws and never blocks longer than <c>timeout</c>, so an import cannot hang on AI.
/// </summary>
public static class LineClassification
{
    public static async Task<IReadOnlyList<Classification>> ClassifyLinesAsync(
        IReadOnlyList<ExtractedLine> lines, IAiClient ai, SeedBundle seed, TimeSpan timeout, CancellationToken ct)
    {
        var results = new Dictionary<string, Classification>(StringComparer.Ordinal);
        var pending = new List<ClassificationInput>();

        foreach (var line in lines)
        {
            var (categoryId, confidence, source, rationale) = InvoiceClassification.Offline(line, seed);
            if (source == ClassificationSource.Stub)
            {
                pending.Add(new ClassificationInput(line.Id, line.Description, line.Quantity, line.UnitCode, line.ClassificationCode, line.ClassificationListId));
                continue;
            }
            results[line.Id] = new Classification(line.Id, categoryId, confidence ?? 0d, rationale, source);
        }

        if (pending.Count > 0)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                var answers = await ai.ClassifyAsync(pending, cts.Token).WaitAsync(timeout, ct);
                var pendingIds = pending.Select(p => p.LineId).ToHashSet(StringComparer.Ordinal);
                foreach (var answer in answers)
                {
                    if (pendingIds.Contains(answer.LineId) && !results.ContainsKey(answer.LineId))
                    {
                        results[answer.LineId] = EnforceAllowList(answer, seed);
                    }
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Timeout, transport error or bad payload: the remaining lines fall through to the stub below.
            }
        }

        return lines.Select(l => results.TryGetValue(l.Id, out var c) ? c : Stub(l.Id)).ToList();
    }

    public static Classification EnforceAllowList(Classification answer, SeedBundle seed) =>
        seed.AllowList.Contains(answer.CategoryId)
            ? answer
            : new Classification(answer.LineId, Ids.Unclassified, 0d, "Category outside the allow-list: " + answer.CategoryId, answer.Source);

    private static Classification Stub(string lineId) =>
        new(lineId, Ids.Unclassified, 0d, "AI unavailable", ClassificationSource.Stub);
}
