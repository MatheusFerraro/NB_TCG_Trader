using NbTcgTrader.Api.Common.Filters;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Marketplace;

/// <summary>
/// Maps the Marketplace slice endpoints (CLAUDE.md §6, BACKLOG #17/#18). Both endpoints
/// expose only public for-sale listings, so they are anonymous and rely on the global
/// rate limiter. The base <c>IsForSale &amp;&amp; !IsPrivate</c> filter is enforced in the
/// handlers and cannot be widened by the caller.
/// </summary>
public static class MarketplaceEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/marketplace")
            .WithTags("Marketplace");

        group.MapGet("/",
                ([AsParameters] BrowseListingsRequest request,
                        BrowseListingsHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("BrowseMarketplaceListings")
            .WithValidation<BrowseListingsRequest>()
            .Produces<CatalogPage<MarketplaceListingResponse>>()
            .AllowAnonymous();

        group.MapGet("/{itemId:int}",
                (int itemId, GetListingHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(itemId, ct))
            .WithName("GetMarketplaceListing")
            .Produces<MarketplaceListingDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        return endpoints;
    }
}
