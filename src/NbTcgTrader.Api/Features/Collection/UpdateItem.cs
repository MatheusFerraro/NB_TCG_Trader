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
/// Full replacement of a binder row's mutable fields (BACKLOG #12): quantity, condition,
/// the for-sale flag and price/currency, the private flag, and notes. The card the row
/// points at never changes — delete and re-add to fix a wrong card. PUT semantics: the
/// client sends the complete desired state, so omitted optionals (price, notes) clear.
/// </summary>
public sealed record UpdateItemRequest(
    int Quantity,
    CardCondition Condition,
    bool IsForSale,
    decimal? Price,
    Currency Currency,
    bool IsPrivate,
    string? Notes);

public sealed class UpdateItemValidator : AbstractValidator<UpdateItemRequest>
{
    public UpdateItemValidator()
    {
        // Same bounds as AddCard, so a row can never be edited into a state it
        // could not have been created in.
        RuleFor(x => x.Quantity).InclusiveBetween(1, 999);
        RuleFor(x => x.Condition).IsInEnum();
        RuleFor(x => x.Currency).IsInEnum();

        // Soft rule from the AC: a listing needs an asking price. A price on an item
        // that is *not* for sale is allowed — users keep asking prices on binder rows
        // they are not currently selling.
        RuleFor(x => x.Price)
            .NotNull()
            .When(x => x.IsForSale)
            .WithMessage("Price is required when the item is for sale.");

        // Bounds match the numeric(18,2) column.
        RuleFor(x => x.Price)
            .GreaterThan(0)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.Price is not null);

        // Max length matches the CollectionItem.Notes column.
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>
/// Applies the update to the caller's own item. Ownership is enforced by filtering on
/// the JWT's user id in the query itself (CLAUDE.md §15); an item that exists but
/// belongs to someone else is indistinguishable from a missing one (404), so the
/// endpoint leaks nothing about other users' binders.
/// </summary>
public sealed class UpdateItemHandler(
    AppDbContext db,
    IOptions<CatalogOptions> catalogOptions,
    ILogger<UpdateItemHandler> logger)
{
    public async Task<IResult> HandleAsync(
        int id,
        UpdateItemRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var item = await db.CollectionItems
            .Include(i => i.Card!)
            .ThenInclude(c => c.CardSet)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);

        if (item is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Collection item not found",
                detail: $"No collection item {id} exists in your binder.");
        }

        item.Quantity = request.Quantity;
        item.Condition = request.Condition;
        item.IsForSale = request.IsForSale;
        item.Price = request.Price;
        item.Currency = request.Currency;
        item.IsPrivate = request.IsPrivate;
        item.Notes = request.Notes;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "User {UserId} updated collection item {ItemId} (forSale={IsForSale}, private={IsPrivate})",
            userId, item.Id, item.IsForSale, item.IsPrivate);

        var response = CollectionItemResponse.From(
            item, catalogOptions.Value.PlaceholderImageUrl);

        return Results.Ok(response);
    }
}
