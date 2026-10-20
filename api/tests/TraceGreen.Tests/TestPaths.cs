namespace TraceGreen.Tests;

/// <summary>Locates the repository's shared seed directory from wherever the tests run.</summary>
public static class TestPaths
{
    public static string SeedDir { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "seed", "factors.json");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "seed");
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("seed/factors.json not found above " + AppContext.BaseDirectory);
    }

    public static string Invoice(string file) => Path.Combine(SeedDir, "invoices", file);
}
