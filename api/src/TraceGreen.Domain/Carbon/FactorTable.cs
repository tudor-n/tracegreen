using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Carbon;

public sealed class FactorNotFoundException(FactorKind kind, string key)
    : Exception($"No {kind} emission factor for key {key}")
{
    public FactorKind Kind { get; } = kind;
    public string Key { get; } = key;
}

/// <summary>Emission factors by kind and key. Every lookup returns the value with its provenance.</summary>
public sealed class FactorTable
{
    private readonly Dictionary<(FactorKind Kind, string Key), Factor> _rows;

    public FactorTable(IEnumerable<Factor> rows)
    {
        _rows = rows.ToDictionary(r => (r.Kind, r.Key));
    }

    public static FactorTable FromSeed(SeedBundle bundle) => new(bundle.Factors);

    public static string TransportKey(TransportMode mode) => mode.ToString().ToLowerInvariant();

    public FactorLookup Lookup(FactorKind kind, string key)
    {
        if (!_rows.TryGetValue((kind, key), out var row))
        {
            throw new FactorNotFoundException(kind, key);
        }
        return new FactorLookup(row.Value, row.Unit, row.Source, row.Year, row.RowLabel);
    }

    public FactorLookup Transport(TransportMode mode) => Lookup(FactorKind.Transport, TransportKey(mode));

    /// <summary>For display only: null when the row is gone, e.g. after <c>factors.json</c> changed under stored nodes.</summary>
    public FactorLookup? TryLookup(FactorKind kind, string key) =>
        _rows.TryGetValue((kind, key), out var row) ? new FactorLookup(row.Value, row.Unit, row.Source, row.Year, row.RowLabel) : null;
}
