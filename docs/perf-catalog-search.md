# Catalog search performance (#48)

Measured 2026-07-07 against a local Development API (`dotnet run`, Release-equivalent
behavior for the HTTP path) calling the live pokemontcg.io v2 API with an API key.
Numbers are end-to-end HTTP round trips against `GET /catalog/cards` as the frontend
issues them (`pageSize=20`).

## Measurements

| Scenario | Latency | Notes |
|---|---|---|
| Broad name (`charizard`), cold | 7.8–8.2 s | Provider round trip; first attempt hit the 5 s per-attempt timeout, retry succeeded |
| Broad name (`pikachu`), cold | 0.8 s | Same query shape — provider latency is highly variable |
| Exact name + set + number, cold | 1.0 s | Small result set (2 matches) |
| Any repeated identical search (cache hit) | 27–59 ms | In-memory cache, 24 h window |
| Next page after a search (prefetched) | 41 ms | Background next-page prefetch landed before the click |
| No-match name, cold | 2.3 s | Returns 200 with `totalCount: 0` |
| Filterless request | **was**: 16.4 s → 503 · **now**: <150 ms → 400 | See "Fix applied" |
| Invalid paging (`page=0`, `pageSize=200`) | 2–34 ms | 400 ProblemDetails from validation, provider never called |

## Where the time goes

The provider **is** the latency; everything local is negligible.

- **pokemontcg.io round trip:** 0.7 s typical, spiking to the resilience pipeline's
  retry path (5 s attempt timeout + retry ≈ 8 s observed; total budget 30 s).
  This dominates every cold search.
- **Local API processing:** cache hits answer in tens of ms — handler, DTO mapping,
  and serialization are noise.
- **Local database:** the search endpoint never touches Postgres. The persistence
  paths used when adding/importing cards (`Card.ExternalId`, `CardSet.ExternalId`,
  `Card (GameId, Name)`, `CardSet (GameId, Code)`, `Game.Slug`) are all covered by
  existing indexes (`CardConfiguration`, `CardSetConfiguration`, `GameConfiguration`).
  No missing index found.
- **Frontend request behavior:** searches fire on explicit submit only (no
  per-keystroke requests), and paging always sends `page`/`pageSize` and renders
  from `totalCount` — pagination is end-to-end; no fetch-all path exists.

## Existing mitigations (verified working)

- **In-memory cache** keyed `catalog:search:{q}|{page}|{pageSize}` (query string is
  the normalized Lucene form, so identical searches share an entry), 24 h absolute
  expiration (`CardApi:CacheMinutes = 1440`). Repeated searches confirmed to hit it.
- **Next-page prefetch** (`CardApi:PrefetchNextPage`) warms `page + 1` in the
  background after every search, so the common "next page" click is a cache hit —
  confirmed (41 ms).
- **Resilience pipeline**: 5 s per-attempt timeout + retries inside a 30 s total
  budget; provider failures surface as a clean 503 ProblemDetails.

## Fix applied

A filterless request mapped to the provider's match-everything query (`q=*`), which
pokemontcg.io cannot answer: it burned the full retry budget (16 s) and returned a
503 every time. The frontend already refuses to submit an empty search, so the API
now rejects filterless requests up front in `CatalogSearchRequestValidator`
(400, "Provide at least one filter") instead of hammering the provider.

Also added an Information-level timing log in `PokemonTcgCatalogClient` for every
provider round trip (query, page, elapsed ms, result counts — no secrets), so
deployed latency is visible in Log Analytics without extra tooling.

## Redis decision: deferred

In-memory caching is sufficient for the deployment shape:

- Single Azure Container Apps instance (no cache to share across replicas).
- Cache hits are 27–59 ms; the pain is the provider's cold latency, which Redis
  would not improve — the first search for a query pays the provider either way.
- Cold starts refill the cache organically per query; a distributed cache would
  only help if instances multiplied or restarts were frequent.

Revisit if: the API scales past one replica, restarts become frequent enough that
users routinely pay cold-search latency, or a second provider/game multiplies the
query space. The `ICardCatalogClient` seam keeps that change local.
