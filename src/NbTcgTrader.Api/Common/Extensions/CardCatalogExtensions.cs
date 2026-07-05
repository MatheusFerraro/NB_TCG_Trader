using System.Net.Http.Headers;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Card-catalog wiring (CLAUDE.md §8, BACKLOG #8): the <see cref="CardCatalogOptions"/>
/// binding, an in-memory cache, and a typed <see cref="HttpClient"/> for
/// <see cref="PokemonTcgCatalogClient"/> wrapped with the standard resilience handler
/// (bounded retries + timeout, honouring <c>Retry-After</c> on 429). The provider
/// base URL and timeout are non-secret config; the API key is a secret supplied via
/// user-secrets / environment variables.
/// </summary>
public static class CardCatalogExtensions
{
    public static IServiceCollection AddApiCardCatalog(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CardCatalogOptions>()
            .Bind(configuration.GetSection(CardCatalogOptions.SectionName))
            .Validate(
                o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "CardApi:BaseUrl must be an absolute URL.")
            .Validate(
                o => o.BaseUrl.EndsWith('/'),
                "CardApi:BaseUrl must end with a trailing slash ('/').")
            .Validate(
                o => o.CacheMinutes > 0,
                "CardApi:CacheMinutes must be positive.")
            .Validate(
                o => o.TimeoutSeconds > 0,
                "CardApi:TimeoutSeconds must be positive.")
            .Validate(
                o => o.AttemptTimeoutSeconds > 0,
                "CardApi:AttemptTimeoutSeconds must be positive.")
            .Validate(
                o => o.MaxPageSize > 0,
                "CardApi:MaxPageSize must be positive.")
            .ValidateOnStart();

        // Catalog presentation options: the placeholder image URL is substituted at the
        // response layer for cards without an image (#9). It must be an absolute URL and
        // differs per environment, so it is validated on start like CardApi:BaseUrl.
        services.AddOptions<CatalogOptions>()
            .Bind(configuration.GetSection(CatalogOptions.SectionName))
            .Validate(
                o => Uri.TryCreate(o.PlaceholderImageUrl, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                "Catalog:PlaceholderImageUrl must be an absolute HTTP(S) URL.")
            .ValidateOnStart();

        // Catalog slice handlers.
        services.AddScoped<SearchCardsHandler>();

        // Shared provider-result persistence: add-to-binder (#10) and import
        // matching (#15) stage catalog rows through the same store.
        services.AddScoped<CatalogCardStore>();

        // Catalog lookups are cached in memory (CLAUDE.md §8). No cache exists yet
        // elsewhere, so register it here; AddMemoryCache is idempotent if reused later.
        services.AddMemoryCache();

        services.AddHttpClient<ICardCatalogClient, PokemonTcgCatalogClient>((serviceProvider, http) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<CardCatalogOptions>>().Value;

                http.BaseAddress = new Uri(options.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                http.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                // pokemontcg.io works without a key at a lower rate limit; send it
                // only when configured. The key is a secret and never logged (§15).
                if (!string.IsNullOrWhiteSpace(options.Key))
                {
                    http.DefaultRequestHeaders.Add("X-Api-Key", options.Key);
                }
            })
            // Bounded retries with exponential backoff + per-try timeout. The standard
            // handler treats 429/5xx/timeouts as transient and respects Retry-After,
            // covering the provider's rate limits (BACKLOG #8).
            .AddStandardResilienceHandler()
            // Fail fast on a single slow try: the provider spikes to 10-30s, so a short
            // per-attempt timeout hands the wait to a retry instead of the user. The
            // pipeline's total budget stays within the HttpClient timeout above.
            .Configure((resilience, serviceProvider) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<CardCatalogOptions>>().Value;

                resilience.AttemptTimeout.Timeout =
                    TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);
                resilience.TotalRequestTimeout.Timeout =
                    TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        return services;
    }
}
