namespace TraceGreen.Domain.Services;

/// <summary>
/// The configuration the use-case services read. The host builds it from its environment (<c>AppConfig</c>), so the
/// Domain keeps no reference to the host project and the services stay testable without a web host.
/// </summary>
public sealed record ServiceOptions(TimeSpan AiTimeout);
