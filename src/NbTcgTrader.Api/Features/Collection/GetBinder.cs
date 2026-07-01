using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Collection;

/// <summary>
/// Query for the owner's binder view (BACKLOG #11), bound from the query string via
/// <c>[AsParameters]</c>. It is the caller's own binder, so private items are included
/// by default; <see cref="IncludePrivate"/> = false narrows to what other users could
/// see (the public/binder-share perspective).
/// </summary>
public sealed record BinderRequest(
    int Page = 1,
    int PageSize = 25,
    bool IncludePrivate = true);

public sealed class BinderRequestValidator : AbstractValidator<BinderRequest>
{
    public BinderRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);

        // Same hard cap as the catalog browse endpoint: a binder grid page never
        // needs more, and it bounds the join the query performs.
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>
/// Returns one page of the caller's binder, newest first, with the card display data
/// each row needs to render in the grid (no client-side catalog join). Owner-scoped:
/// the user id comes from the JWT, so only the caller's own items are ever queried
/// (CLAUDE.md §15).
/// </summary>
public sealed class GetBinderHandler(
    AppDbContext db,
    IOptions<CatalogOptions> catalogOptions)
{
    public async Task<IResult> HandleAsync(
        BinderRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var query = db.CollectionItems
            .AsNoTracking()
            .Where(i => i.UserId == userId);

        if (!request.IncludePrivate)
        {
            query = query.Where(i => !i.IsPrivate);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id) // tie-break so paging is deterministic
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(i => i.Card!)
            .ThenInclude(c => c.CardSet)
            .ToListAsync(cancellationToken);

        var placeholder = catalogOptions.Value.PlaceholderImageUrl;
        var responses = items
            .Select(i => CollectionItemResponse.From(i, placeholder))
            .ToList();

        return Results.Ok(new CatalogPage<CollectionItemResponse>(
            responses, request.Page, request.PageSize, totalCount));
    }
}
