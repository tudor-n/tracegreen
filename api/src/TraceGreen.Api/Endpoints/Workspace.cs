using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Api.Endpoints;

/// <summary>Caller contexts until authentication exists: the one demo workspace, with the actor named by role.</summary>
public static class Workspace
{
    public static Ctx Buyer(SeedBundle seed) => new(seed.DemoState.WorkspaceId, "buyer");

    public static Ctx Demo(SeedBundle seed) => new(seed.DemoState.WorkspaceId, "demo");
}
