using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Tests;

/// <summary>
/// In-memory <see cref="ICardCatalogClient"/> double for handler/endpoint tests: it
/// captures the last query it was called with and returns a canned page, so the catalog
/// slice can be exercised without touching the network or the real provider client.
/// </summary>
internal sealed class FakeCardCatalogClient : ICardCatalogClient
{
    public CatalogSearchQuery? LastQuery { get; private set; }

    public CatalogPage<CatalogCard> NextPage { get; set; } =
        new(Array.Empty<CatalogCard>(), 1, 25, 0);

    public Task<CatalogPage<CatalogCard>> SearchCardsAsync(
        CatalogSearchQuery query, CancellationToken cancellationToken)
    {
        LastQuery = query;
        return Task.FromResult(NextPage);
    }

    public Task<CatalogCard?> GetCardAsync(string externalId, CancellationToken cancellationToken) =>
        Task.FromResult<CatalogCard?>(null);
}
