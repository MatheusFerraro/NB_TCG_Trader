using NbTcgTrader.Api.Common.Filters;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Marketplace;

/// <summary>
/// Maps the Marketplace slice endpoints (CLAUDE.md §6, BACKLOG #17). Browsing exposes
/// only public for-sale listings, so the endpoint is anonymous and relies on the global
/// rate limiter. The base <c>IsForSale &amp;&amp; !IsPrivate</c> filter is enforced in the
/// handler and cannot be widened by the caller.
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

        return endpoints;
    }
}
