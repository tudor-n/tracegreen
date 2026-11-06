using System.Security.Cryptography;
using TraceGreen.Domain.Ai;

namespace TraceGreen.Domain.Services;

/// <summary>A PDF's extracted fields waiting for a person to check them (F9), with the PDF's hash and who extracted them.</summary>
public sealed record PdfDraft(string Id, Guid WorkspaceId, string DocumentSha256, DraftInvoice Invoice, IReadOnlyDictionary<string, bool> Found, string Provider, int Pages, DateTimeOffset ExpiresAt);

/// <summary>
/// Drafts in memory, by a random id, for <see cref="Ttl"/> and at most <see cref="MaxDrafts"/> at a time (the oldest go
/// first). They live in this process only, like the event bus: a restart drops them and the confirm answers 410.
/// Holding the draft here means the person's edits are compared with what the model really answered, not with what a
/// client claims it answered.
/// </summary>
public sealed class PdfDraftStore(TimeProvider clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    public const int MaxDrafts = 32;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, PdfDraft> _drafts = new(StringComparer.Ordinal);

    public PdfDraft Add(Guid workspaceId, string documentSha256, DraftInvoice invoice, IReadOnlyDictionary<string, bool> found, string provider, int pages)
    {
        var draft = new PdfDraft(
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), workspaceId, documentSha256, invoice, found, provider, pages,
            clock.GetUtcNow() + Ttl);
        lock (_gate)
        {
            Prune();
            while (_drafts.Count >= MaxDrafts)
            {
                _drafts.Remove(_drafts.Values.MinBy(d => d.ExpiresAt)!.Id);
            }
            _drafts[draft.Id] = draft;
        }
        return draft;
    }

    /// <summary>The draft, or null when it is unknown, expired or of another workspace.</summary>
    public PdfDraft? Find(Guid workspaceId, string id)
    {
        lock (_gate)
        {
            Prune();
            return _drafts.TryGetValue(id, out var draft) && draft.WorkspaceId == workspaceId ? draft : null;
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _drafts.Remove(id);
        }
    }

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var id in _drafts.Values.Where(d => d.ExpiresAt <= now).Select(d => d.Id).ToList())
        {
            _drafts.Remove(id);
        }
    }
}
