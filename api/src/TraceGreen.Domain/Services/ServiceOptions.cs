namespace TraceGreen.Domain.Services;

/// <summary>
/// The configuration the use-case services read. The host builds it from its environment (<c>AppConfig</c>), so the
/// Domain keeps no reference to the host project and the services stay testable without a web host.
/// <see cref="IpHashKey"/> keys <c>Hashing.IpHash</c>, so a stored verification holds no client address.
/// <see cref="EtsPriceEurPerT"/> is the carbon price the MAC badge compares against (<c>ETS_PRICE_EUR_PER_T</c>).
/// </summary>
public sealed record ServiceOptions(TimeSpan AiTimeout, TimeSpan LinkTtl, string IpHashKey, decimal EtsPriceEurPerT);
