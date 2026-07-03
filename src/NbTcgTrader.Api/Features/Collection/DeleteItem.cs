using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Collection;

/// <summary>
/// Hard-deletes a binder row (BACKLOG #12). Ownership is enforced in the delete
/// predicate itself — the JWT's user id is part of the WHERE clause — so someone
/// else's item id deletes nothing and returns the same 404 as a missing id
/// (CLAUDE.md §15). A single ExecuteDelete round-trip; no entity is loaded.
/// </summary>
public sealed class DeleteItemHandler(
    AppDbContext db,
    ILogger<DeleteItemHandler> logger)
{
    public async Task<IResult> HandleAsync(
        int id,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var deleted = await db.CollectionItems
            .Where(i => i.Id == id && i.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Collection item not found",
                detail: $"No collection item {id} exists in your binder.");
        }

        logger.LogInformation(
            "User {UserId} deleted collection item {ItemId}", userId, id);

        return Results.NoContent();
    }
}
