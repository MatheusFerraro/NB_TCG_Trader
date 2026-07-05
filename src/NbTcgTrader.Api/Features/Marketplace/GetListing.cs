using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Marketplace;

/// <summary>
/// Returns one public for-sale listing with the seller's public contact channels
/// (BACKLOG #18). The <c>IsForSale &amp;&amp; !IsPrivate</c> guard sits in the WHERE
/// clause itself, so a private or not-for-sale item is indistinguishable from a missing
/// one — both 404 (CLAUDE.md §15). Only the seller's deliberately-public fields
/// (DisplayName, City, Country, ContactEmail, DiscordHandle, InstagramHandle) are
/// projected; nothing else on <see cref="AppUser"/> can leak because the SQL never
/// selects it.
/// </summary>
public sealed class GetListingHandler(
    AppDbContext db,
    IOptions<CatalogOptions> catalogOptions)
{
    public async Task<IResult> HandleAsync(
        int itemId,
        CancellationToken cancellationToken)
    {
        var row = await db.CollectionItems
            .AsNoTracking()
            .Where(i => i.Id == itemId && i.IsForSale && !i.IsPrivate)
            .Select(i => new ListingDetailRow(
                i.Id,
                i.Card!.Id,
                i.Card.ExternalId,
                i.Card.Name,
                i.Card.Number,
                i.Card.Rarity,
                i.Card.ImageUrl,
                i.Card.CardSet == null ? null : i.Card.CardSet.Name,
                i.Card.Game!.Name,
                i.Quantity,
                i.Condition,
                i.Price,
                i.Currency,
                i.Notes,
                i.User!.DisplayName,
                i.User.City,
                i.User.Country,
                i.User.ContactEmail,
                i.User.DiscordHandle,
                i.User.InstagramHandle,
                i.CreatedAt,
                i.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Listing not found",
                detail: $"No marketplace listing {itemId} exists.");
        }

        return Results.Ok(row.ToResponse(catalogOptions.Value.PlaceholderImageUrl));
    }

    private sealed record ListingDetailRow(
        int Id,
        int CardId,
        string CardExternalId,
        string CardName,
        string? CardNumber,
        string? CardRarity,
        string? CardImageUrl,
        string? SetName,
        string GameName,
        int Quantity,
        CardCondition Condition,
        decimal? Price,
        Currency Currency,
        string? Notes,
        string SellerDisplayName,
        string? SellerCity,
        string? SellerCountry,
        string? SellerContactEmail,
        string? SellerDiscordHandle,
        string? SellerInstagramHandle,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt)
    {
        public MarketplaceListingDetailResponse ToResponse(string placeholderImageUrl)
        {
            var hasImage = !string.IsNullOrWhiteSpace(CardImageUrl);
            return new MarketplaceListingDetailResponse(
                Id,
                new MarketplaceCardResponse(
                    CardId,
                    CardExternalId,
                    CardName,
                    CardNumber,
                    CardRarity,
                    hasImage ? CardImageUrl! : placeholderImageUrl,
                    hasImage,
                    SetName,
                    GameName),
                Quantity,
                Condition,
                Price,
                Currency,
                Notes,
                new MarketplaceSellerContactResponse(
                    SellerDisplayName,
                    SellerCity,
                    SellerCountry,
                    SellerContactEmail,
                    SellerDiscordHandle,
                    SellerInstagramHandle),
                CreatedAt,
                UpdatedAt);
        }
    }
}
