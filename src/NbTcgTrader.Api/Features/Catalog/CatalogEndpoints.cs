using NbTcgTrader.Api.Common.Filters;

namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// Maps the Catalog slice endpoints (CLAUDE.md §6, BACKLOG #9). Browsing exposes only
/// public external card data, so the search endpoint is anonymous; it relies on the
/// lenient global rate limiter (results are cached, CLAUDE.md §8). Adding a card to a
/// collection is a separate, authorized slice.
/// </summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/catalog")
            .WithTags("Catalog");

        group.MapGet("/cards",
                ([AsParameters] CatalogSearchRequest request,
                        SearchCardsHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("SearchCatalogCards")
            .WithValidation<CatalogSearchRequest>()
            .Produces<CatalogPage<CatalogCardResponse>>()
            .AllowAnonymous();

        return endpoints;
    }
}
