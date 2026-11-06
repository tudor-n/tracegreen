namespace TraceGreen.Domain.Services;

/// <summary>
/// The configuration the use-case services read. The host builds it from its environment (<c>AppConfig</c>), so the
/// Domain keeps no reference to the host project and the services stay testable without a web host.
/// <see cref="IpHashKey"/> keys <c>Hashing.IpHash</c>, so a stored verification holds no client address.
/// <see cref="EtsPriceEurPerT"/> is the carbon price the MAC badge compares against (<c>ETS_PRICE_EUR_PER_T</c>).
/// <see cref="AiTimeout"/> bounds classification and suggestions; <see cref="AiChatTimeout"/> and
/// <see cref="AiExtractTimeout"/> bound the longer chat (F13) and PDF extraction (F9) calls, which no import waits on.
/// </summary>
public sealed record ServiceOptions(
    TimeSpan AiTimeout,
    TimeSpan LinkTtl,
    string IpHashKey,
    decimal EtsPriceEurPerT,
    TimeSpan AiChatTimeout,
    TimeSpan AiExtractTimeout);
