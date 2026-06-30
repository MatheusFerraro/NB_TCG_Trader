namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// A best-effort search against the external catalog. <see cref="Name"/>,
/// <see cref="Set"/>, and <see cref="Number"/> are optional filters that combine;
/// at least one is normally supplied by the caller.
/// </summary>
public sealed record CatalogSearchQuery(
    string? Name = null,
    string? Set = null,
    string? Number = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>
/// A set/expansion as returned by the provider. Provider-agnostic projection that
/// maps onto the domain <c>CardSet</c> (CLAUDE.md §7) via <c>CatalogMapping</c>.
/// </summary>
public sealed record CatalogSet(
    string ExternalId,
    string Name,
    string Code,
    DateOnly? ReleaseDate);

/// <summary>
/// A single catalog card as returned by the provider. Provider-agnostic projection
/// that maps onto the domain <c>Card</c> (CLAUDE.md §7) via <c>CatalogMapping</c>.
/// <see cref="ImageUrl"/> is a remote URL to hotlink, never a stored blob (§8).
/// </summary>
public sealed record CatalogCard(
    string ExternalId,
    string Name,
    string? Number,
    string? Rarity,
    string? ImageUrl,
    string? Metadata,
    CatalogSet? Set);

/// <summary>One page of catalog results plus the provider's paging metadata.</summary>
public sealed record CatalogPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// Reads card data from an external provider (CLAUDE.md §8). pokemontcg.io is the
/// primary implementation; TCGdex is the documented fallback behind the same
/// interface so the provider can be swapped without touching callers. Implementations
/// cache lookups and respect provider rate limits.
/// </summary>
public interface ICardCatalogClient
{
    /// <summary>Searches cards by name/set/number, returning a page of results.</summary>
    Task<CatalogPage<CatalogCard>> SearchCardsAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken);

    /// <summary>Fetches a single card by its provider id, or <c>null</c> if absent.</summary>
    Task<CatalogCard?> GetCardAsync(string externalId, CancellationToken cancellationToken);
}
