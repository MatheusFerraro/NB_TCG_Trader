using System.Security.Claims;
using NbTcgTrader.Api.Common.Filters;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Collection;

/// <summary>
/// Maps the Collection/binder slice endpoints (CLAUDE.md §6, BACKLOG #10/#11/#12).
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

        group.MapPut("/items/{id:int}",
                (int id, UpdateItemRequest request, ClaimsPrincipal principal,
                        UpdateItemHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(id, request, principal, ct))
            .WithName("UpdateCollectionItem")
            .WithValidation<UpdateItemRequest>()
            .Produces<CollectionItemResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/items/{id:int}",
                (int id, ClaimsPrincipal principal,
                        DeleteItemHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(id, principal, ct))
            .WithName("DeleteCollectionItem")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
