using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Marketplace;

/// <summary>
/// A public for-sale listing as returned by the marketplace browse endpoint
/// (BACKLOG #17). A listing is a <see cref="CollectionItem"/> that is
/// <c>IsForSale &amp;&amp; !IsPrivate</c>. Only public, non-sensitive seller data is
/// exposed here (display name + location); the contact channels are revealed by the
/// separate listing-detail endpoint (#18). Enums serialize as strings ("NM", "CAD").
/// </summary>
public sealed record MarketplaceListingResponse(
    int Id,
    MarketplaceCardResponse Card,
    int Quantity,
    CardCondition Condition,
    decimal? Price,
    Currency Currency,
    string? Notes,
    MarketplaceSellerResponse Seller,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Display projection of the catalog <see cref="Card"/> a listing points at, with the
/// game and set names a marketplace grid shows. <see cref="ImageUrl"/> is never null:
/// a configured placeholder is substituted and <see cref="HasImage"/> flags the fallback,
/// matching the binder/catalog DTOs.
/// </summary>
public sealed record MarketplaceCardResponse(
    int Id,
    string ExternalId,
    string Name,
    string? Number,
    string? Rarity,
    string ImageUrl,
    bool HasImage,
    string? SetName,
    string GameName);

/// <summary>
/// The public face of the seller on a browse card: a display name and location so a
/// buyer can gauge distance. Deliberately excludes the contact channels
/// (email/Discord/Instagram) — those are revealed only on the listing-detail endpoint
/// (#18), per CLAUDE.md §15.
/// </summary>
public sealed record MarketplaceSellerResponse(
    string DisplayName,
    string? City,
    string? Country);
