using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Tests;

/// <summary>
/// In-memory <see cref="ICardCatalogClient"/> double for handler/endpoint tests: it
/// captures the calls it receives and returns canned data, so the catalog-consuming
/// slices can be exercised without touching the network or the real provider client.
/// Single-card lookups are served from <see cref="Cards"/> (unknown id → null, like
/// the provider's 404) and recorded in <see cref="GetCardRequests"/>.
/// </summary>
internal sealed class FakeCardCatalogClient : ICardCatalogClient
{
    public CatalogSearchQuery? LastQuery { get; private set; }

    public CatalogPage<CatalogCard> NextPage { get; set; } =
        new(Array.Empty<CatalogCard>(), 1, 25, 0);

    public Dictionary<string, CatalogCard> Cards { get; } = [];

    public List<string> GetCardRequests { get; } = [];

    public Task<CatalogPage<CatalogCard>> SearchCardsAsync(
        CatalogSearchQuery query, CancellationToken cancellationToken)
    {
        LastQuery = query;
        return Task.FromResult(NextPage);
    }

    public Task<CatalogCard?> GetCardAsync(string externalId, CancellationToken cancellationToken)
    {
        GetCardRequests.Add(externalId);
        return Task.FromResult(Cards.TryGetValue(externalId, out var card) ? card : null);
    }
}
