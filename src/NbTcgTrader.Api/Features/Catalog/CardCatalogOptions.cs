namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// External card-data provider settings bound from the <c>CardApi</c> configuration
/// section (CLAUDE.md §8, §13). <see cref="Key"/> is a secret and is never committed:
/// it comes from user-secrets locally and environment variables in deployed
/// environments. The non-secret defaults live in <c>appsettings.json</c>.
/// </summary>
public sealed class CardCatalogOptions
{
    public const string SectionName = "CardApi";

    /// <summary>
    /// Optional pokemontcg.io API key, sent as the <c>X-Api-Key</c> header. The
    /// provider works without one at a lower rate limit, so this is not required.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>Base address of the provider; must end with a trailing slash.</summary>
    public string BaseUrl { get; init; } = "https://api.pokemontcg.io/v2/";

    /// <summary>How long catalog lookups stay cached in memory (CLAUDE.md §8).</summary>
    public int CacheMinutes { get; init; } = 1440;

    /// <summary>Per-request HTTP timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Upper bound on the page size requested from the provider.</summary>
    public int MaxPageSize { get; init; } = 50;
}
