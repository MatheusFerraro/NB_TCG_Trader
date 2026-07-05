using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Errors;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// <see cref="ICardCatalogClient"/> backed by pokemontcg.io (CLAUDE.md §8). A typed
/// <see cref="HttpClient"/> (base address, timeout, and API key are configured by the
/// DI registration) deserializes the provider's JSON and maps it to the provider-
/// agnostic result records. Lookups are cached in memory; rate limits and transient
/// failures are handled by the resilience handler wired in <c>CardCatalogExtensions</c>.
/// </summary>
public sealed class PokemonTcgCatalogClient(
    HttpClient http,
    IMemoryCache cache,
    IOptions<CardCatalogOptions> options,
    ILogger<PokemonTcgCatalogClient> logger) : ICardCatalogClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// pokemontcg.io <c>select</c> projection: only the fields <see cref="MapCard"/>
    /// consumes, so the provider returns a much smaller payload.
    /// </summary>
    private const string SelectFields = "id,name,number,rarity,images,set";

    /// <summary>Upper bound on a background next-page prefetch (never a user's token).</summary>
    private static readonly TimeSpan PrefetchTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Cache keys with a prefetch currently in flight, so concurrent prefetches for the
    /// same page don't stampede the provider. Static because the typed client is
    /// transient while the warmed <see cref="IMemoryCache"/> is a singleton.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> InFlightPrefetches = new();

    private readonly CardCatalogOptions _options = options.Value;

    public async Task<CatalogPage<CatalogCard>> SearchCardsAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, _options.MaxPageSize);
        var q = BuildLuceneQuery(query);

        var result = await SearchPageAsync(q, page, pageSize, cancellationToken);

        // Warm the next page in the background: the provider is slow (10-30s spikes),
        // so the follow-up "next page" click should hit the cache instead.
        if (_options.PrefetchNextPage && page * pageSize < result.TotalCount)
        {
            PrefetchPage(q, page + 1, pageSize);
        }

        return result;
    }

    /// <summary>
    /// The single fetch path for a search page — both user requests and background
    /// prefetches go through here so the cache key format stays identical.
    /// </summary>
    private Task<CatalogPage<CatalogCard>> SearchPageAsync(
        string q,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"catalog:search:{q}|{page}|{pageSize}";

        return GetOrCreateAsync(cacheKey, async () =>
        {
            var url = QueryHelpers.AddQueryString("cards", new Dictionary<string, string?>
            {
                ["q"] = q,
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
                ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture),
                ["select"] = SelectFields,
            });

            var envelope = await http.GetFromJsonAsync<CardsEnvelope>(url, JsonOptions, cancellationToken)
                           ?? new CardsEnvelope(null, page, pageSize, 0);

            var items = (envelope.Data ?? []).Select(MapCard).ToArray();
            logger.LogDebug(
                "pokemontcg.io search returned {Count} card(s) (totalCount {TotalCount})",
                items.Length, envelope.TotalCount);

            return new CatalogPage<CatalogCard>(
                items,
                envelope.Page == 0 ? page : envelope.Page,
                envelope.PageSize == 0 ? pageSize : envelope.PageSize,
                envelope.TotalCount);
        });
    }

    /// <summary>
    /// Fire-and-forget cache warming for a search page. Skipped when the page is
    /// already cached or another prefetch for the same key is in flight. Runs on its
    /// own short timeout — never the request's token — and swallows every failure:
    /// a missed prefetch only means the next page loads at provider speed.
    /// </summary>
    private void PrefetchPage(string q, int page, int pageSize)
    {
        var cacheKey = $"catalog:search:{q}|{page}|{pageSize}";

        if (cache.TryGetValue(cacheKey, out _) || !InFlightPrefetches.TryAdd(cacheKey, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(PrefetchTimeout);
                await SearchPageAsync(q, page, pageSize, cts.Token);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Next-page prefetch failed for {CacheKey}", cacheKey);
            }
            finally
            {
                InFlightPrefetches.TryRemove(cacheKey, out _);
            }
        });
    }

    public Task<CatalogCard?> GetCardAsync(string externalId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        return GetOrCreateAsync<CatalogCard?>($"catalog:card:{externalId}", async () =>
        {
            // Provider ids are url-safe, but escape defensively rather than concatenating.
            var url = QueryHelpers.AddQueryString(
                $"cards/{Uri.EscapeDataString(externalId)}", "select", SelectFields);
            using var response = await http.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var envelope = await response.Content
                .ReadFromJsonAsync<CardEnvelope>(JsonOptions, cancellationToken);

            return envelope?.Data is { } dto ? MapCard(dto) : null;
        });
    }

    /// <summary>
    /// Builds a pokemontcg.io Lucene query from the optional filters. Values are
    /// quoted so embedded spaces are treated as a phrase; a bare <c>*</c> matches
    /// everything when no filter is supplied.
    /// </summary>
    private static string BuildLuceneQuery(CatalogSearchQuery query)
    {
        var clauses = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            clauses.Add($"name:{Quote(query.Name)}");
        }

        if (!string.IsNullOrWhiteSpace(query.Set))
        {
            // The caller may pass a set id (e.g. "base1") or a set name (e.g. "Base").
            clauses.Add($"(set.id:{Quote(query.Set)} OR set.name:{Quote(query.Set)})");
        }

        if (!string.IsNullOrWhiteSpace(query.Number))
        {
            clauses.Add($"number:{Quote(query.Number)}");
        }

        return clauses.Count == 0 ? "*" : string.Join(' ', clauses);
    }

    /// <summary>Quotes a Lucene term value, stripping embedded quotes that would break it.</summary>
    private static string Quote(string value) =>
        $"\"{value.Trim().Replace("\"", string.Empty)}\"";

    private static CatalogCard MapCard(CardDto dto) => new(
        dto.Id,
        dto.Name,
        dto.Number,
        dto.Rarity,
        dto.Images?.Large ?? dto.Images?.Small,
        BuildMetadata(dto),
        MapSet(dto.Set));

    private static CatalogSet? MapSet(SetDto? set) => set is null
        ? null
        : new CatalogSet(
            set.Id,
            set.Name,
            string.IsNullOrWhiteSpace(set.PtcgoCode) ? set.Id : set.PtcgoCode,
            ParseReleaseDate(set.ReleaseDate));

    private static DateOnly? ParseReleaseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy/MM/dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>
    /// Captures the variable per-TCG attributes as a small JSON object for the domain
    /// <c>Card.Metadata</c> (jsonb) column. Returns <c>null</c> when nothing is present.
    /// </summary>
    private static string? BuildMetadata(CardDto dto)
    {
        var metadata = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(dto.Supertype))
        {
            metadata["supertype"] = dto.Supertype;
        }

        if (dto.Subtypes is { Count: > 0 })
        {
            metadata["subtypes"] = dto.Subtypes;
        }

        if (!string.IsNullOrWhiteSpace(dto.Hp))
        {
            metadata["hp"] = dto.Hp;
        }

        if (dto.Types is { Count: > 0 })
        {
            metadata["types"] = dto.Types;
        }

        return metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private async Task<T> GetOrCreateAsync<T>(string cacheKey, Func<Task<T>> factory)
    {
        try
        {
            return (await cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheMinutes);
                return await factory();
            }))!;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            // Expected operational failure (provider slow, down, or rate-limited after
            // retries) — surface it as a client-safe 503 instead of an unhandled 500.
            logger.LogWarning(ex, "Card catalog provider request failed for {CacheKey}", cacheKey);
            throw new UpstreamUnavailableException(
                "Card catalog temporarily unavailable",
                "The card catalog did not respond. Please try again in a moment.",
                ex);
        }
    }

    /// <summary>
    /// Failures the resilience pipeline gives up on: transport errors, the pipeline's
    /// total timeout, an open circuit, or HttpClient's own timeout (TaskCanceled while
    /// the caller has NOT cancelled — a user-aborted request must propagate as-is).
    /// </summary>
    private static bool IsProviderFailure(Exception ex) => ex switch
    {
        HttpRequestException or TimeoutRejectedException or BrokenCircuitException => true,
        TaskCanceledException tce => tce.CancellationToken.IsCancellationRequested is false
                                     || tce.InnerException is TimeoutException,
        _ => false,
    };

    // --- Provider JSON shapes (pokemontcg.io v2). Internal to the mapping above. ---

    private sealed record CardsEnvelope(List<CardDto>? Data, int Page, int PageSize, int TotalCount);

    private sealed record CardEnvelope(CardDto? Data);

    private sealed record CardDto(
        string Id,
        string Name,
        string? Number,
        string? Rarity,
        string? Supertype,
        List<string>? Subtypes,
        string? Hp,
        List<string>? Types,
        SetDto? Set,
        ImagesDto? Images);

    private sealed record SetDto(string Id, string Name, string? PtcgoCode, string? ReleaseDate);

    private sealed record ImagesDto(string? Small, string? Large);
}
