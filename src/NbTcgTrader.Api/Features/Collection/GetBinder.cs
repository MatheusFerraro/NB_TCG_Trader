using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Domain;
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
    public const int MaxPage = 10_000;

    public BinderRequestValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, MaxPage);

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

        var placeholder = catalogOptions.Value.PlaceholderImageUrl;
        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id) // tie-break so paging is deterministic
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(i => new BinderRow(
                i.Id,
                i.Card!.Id,
                i.Card.ExternalId,
                i.Card.Name,
                i.Card.Number,
                i.Card.Rarity,
                i.Card.ImageUrl,
                i.Card.CardSet == null ? null : i.Card.CardSet.Name,
                i.Quantity,
                i.Condition,
                i.IsForSale,
                i.Price,
                i.Currency,
                i.IsPrivate,
                i.Notes,
                i.CreatedAt,
                i.UpdatedAt))
            .ToListAsync(cancellationToken);

        var responses = rows
            .Select(i => i.ToResponse(placeholder))
            .ToList();

        return Results.Ok(new CatalogPage<CollectionItemResponse>(
            responses, request.Page, request.PageSize, totalCount));
    }

    private sealed record BinderRow(
        int Id,
        int CardId,
        string CardExternalId,
        string CardName,
        string? CardNumber,
        string? CardRarity,
        string? CardImageUrl,
        string? SetName,
        int Quantity,
        CardCondition Condition,
        bool IsForSale,
        decimal? Price,
        Currency Currency,
        bool IsPrivate,
        string? Notes,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt)
    {
        public CollectionItemResponse ToResponse(string placeholderImageUrl)
        {
            var hasImage = !string.IsNullOrWhiteSpace(CardImageUrl);
            return new CollectionItemResponse(
                Id,
                new CollectionCardResponse(
                    CardId,
                    CardExternalId,
                    CardName,
                    CardNumber,
                    CardRarity,
                    hasImage ? CardImageUrl! : placeholderImageUrl,
                    hasImage,
                    SetName),
                Quantity,
                Condition,
                IsForSale,
                Price,
                Currency,
                IsPrivate,
                Notes,
                CreatedAt,
                UpdatedAt);
        }
    }
}
