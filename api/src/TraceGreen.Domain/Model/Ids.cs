namespace TraceGreen.Domain.Model;

public static class Ids
{
    /// <summary>The single workspace used until authentication exists.</summary>
    public static readonly Guid DemoWorkspace = Guid.Parse("00000000-0000-4000-8000-000000000001");

    /// <summary>Category assigned when no classification can be trusted.</summary>
    public const string Unclassified = "UNCLASSIFIED";

    public static Guid NewId() => Guid.CreateVersion7();
}
