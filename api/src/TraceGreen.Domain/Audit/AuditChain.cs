using System.Text.Json.Nodes;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Security;

namespace TraceGreen.Domain.Audit;

public sealed record ChainVerification(bool Valid, int Length, int? BrokenAt);

/// <summary>
/// Hash-chained, append-only audit log (C4). Each entry's hash covers the previous hash and the canonical JSON of
/// <c>{ ts, actor, action, payload }</c>, so editing, reordering or deleting any entry breaks every later hash.
/// </summary>
public static class AuditChain
{
    public static readonly string GenesisHash = new('0', 64);

    public static string ComputeHash(string prevHash, JsonNode envelope) =>
        Hashing.Sha256Hex(prevHash + CanonicalJson.Serialize(envelope));

    /// <summary>Timestamps are stored at millisecond precision in UTC so a database round trip cannot change the hash input.</summary>
    public static DateTimeOffset Normalize(DateTimeOffset ts) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ts.ToUnixTimeMilliseconds());

    public static JsonObject Envelope(DateTimeOffset ts, string actor, string action, JsonNode? payload) => new()
    {
        ["ts"] = Normalize(ts).ToString("O"),
        ["actor"] = actor,
        ["action"] = action,
        ["payload"] = payload?.DeepClone(),
    };

    public static NewAuditEntry Build(AuditEntry? prev, Guid workspaceId, DateTimeOffset ts, string actor, string action, JsonNode payload)
    {
        var normalized = Normalize(ts);
        var prevHash = prev?.Hash ?? GenesisHash;
        var hash = ComputeHash(prevHash, Envelope(normalized, actor, action, payload));
        return new NewAuditEntry(workspaceId, normalized, actor, action, payload, prevHash, hash);
    }

    public static AuditEntry WithId(NewAuditEntry entry, long id) =>
        new(id, entry.WorkspaceId, entry.Ts, entry.Actor, entry.Action, entry.Payload, entry.PrevHash, entry.Hash);

    public static ChainVerification Verify(IReadOnlyList<AuditEntry> entries)
    {
        var prevHash = GenesisHash;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!string.Equals(entry.PrevHash, prevHash, StringComparison.Ordinal))
            {
                return new ChainVerification(false, entries.Count, i + 1);
            }
            var expected = ComputeHash(prevHash, Envelope(entry.Ts, entry.Actor, entry.Action, entry.Payload));
            if (!string.Equals(entry.Hash, expected, StringComparison.Ordinal))
            {
                return new ChainVerification(false, entries.Count, i + 1);
            }
            prevHash = entry.Hash;
        }
        return new ChainVerification(true, entries.Count, null);
    }
}
