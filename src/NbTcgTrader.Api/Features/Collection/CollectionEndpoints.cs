using System.Security.Claims;
using NbTcgTrader.Api.Common.Filters;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Collection;

/// <summary>
/// Maps the Collection/binder slice endpoints (CLAUDE.md §6, BACKLOG #10/#11).
/// Everything under <c>/collection</c> is owner-scoped, so the whole group requires
/// authorization; handlers take the owner from the JWT's <c>sub</c> claim.
/// </summary>
public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/collection")
            .WithTags("Collection")
            .RequireAuthorization();

        group.MapPost("/items",
                (AddCardRequest request, ClaimsPrincipal principal,
                        AddCardHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, principal, ct))
            .WithName("AddCollectionItem")
            .WithValidation<AddCardRequest>()
            .Produces<CollectionItemResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/me",
                ([AsParameters] BinderRequest request, ClaimsPrincipal principal,
                        GetBinderHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, principal, ct))
            .WithName("GetMyBinder")
            .WithValidation<BinderRequest>()
            .Produces<CatalogPage<CollectionItemResponse>>();

        return endpoints;
    }
}
