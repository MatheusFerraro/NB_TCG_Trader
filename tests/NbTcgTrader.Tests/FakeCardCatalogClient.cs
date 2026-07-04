using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Tests;

/// <summary>
/// In-memory <see cref="ICardCatalogClient"/> double for handler/endpoint tests: it
/// captures the calls it receives and returns canned data, so the catalog-consuming
/// slices can be exercised without touching the network or the real provider client.
/// Single-card lookups are served from <see cref="Cards"/> (unknown id → null, like
/// the provider's 404) and recorded in <see cref="GetCardRequests"/>. Searches return
/// the canned <see cref="NextPage"/> by default; with <see cref="SearchFromCards"/>
/// they instead filter <see cref="Cards"/> by name/set/number (case-insensitive
/// equality, set matched on name or code), which is what import matching (#15) needs
/// to exercise exact/unique/ambiguous scenarios.
/// </summary>
internal sealed class FakeCardCatalogClient : ICardCatalogClient
{
    public CatalogSearchQuery? LastQuery { get; private set; }

    public CatalogPage<CatalogCard> NextPage { get; set; } =
        new(Array.Empty<CatalogCard>(), 1, 25, 0);

    public bool SearchFromCards { get; set; }

    public Exception? SearchFailure { get; set; }

    public List<CatalogSearchQuery> SearchQueries { get; } = [];

    public Dictionary<string, CatalogCard> Cards { get; } = [];

    public List<string> GetCardRequests { get; } = [];

    public Task<CatalogPage<CatalogCard>> SearchCardsAsync(
        CatalogSearchQuery query, CancellationToken cancellationToken)
    {
        LastQuery = query;
        SearchQueries.Add(query);

        if (SearchFailure is not null)
        {
            throw SearchFailure;
        }

        if (!SearchFromCards)
        {
            return Task.FromResult(NextPage);
        }

        IEnumerable<CatalogCard> hits = Cards.Values;
        if (query.Name is not null)
        {
            hits = hits.Where(c =>
                string.Equals(c.Name, query.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Set is not null)
        {
            hits = hits.Where(c => c.Set is not null
                && (string.Equals(c.Set.Name, query.Set, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Set.Code, query.Set, StringComparison.OrdinalIgnoreCase)));
        }

        if (query.Number is not null)
        {
            hits = hits.Where(c =>
                string.Equals(c.Number, query.Number, StringComparison.OrdinalIgnoreCase));
        }

        var matches = hits.ToList();
        return Task.FromResult(new CatalogPage<CatalogCard>(
            matches.Take(query.PageSize).ToList(), query.Page, query.PageSize, matches.Count));
    }

    public Task<CatalogCard?> GetCardAsync(string externalId, CancellationToken cancellationToken)
    {
        GetCardRequests.Add(externalId);
        return Task.FromResult(Cards.TryGetValue(externalId, out var card) ? card : null);
    }
}
