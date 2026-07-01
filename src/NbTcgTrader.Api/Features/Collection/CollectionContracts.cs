using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Collection;

/// <summary>
/// A binder row as returned to its owner (BACKLOG #10/#11). Includes the card display
/// data the binder grid needs, so the client never has to join against the catalog.
/// Enums serialize as strings ("NM", "CAD") via the global JSON options.
/// </summary>
public sealed record CollectionItemResponse(
    int Id,
    CollectionCardResponse Card,
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
    /// <summary>Maps an item whose <c>Card</c> (and its <c>CardSet</c>) navigation is loaded.</summary>
    public static CollectionItemResponse From(CollectionItem item, string placeholderImageUrl)
    {
        var card = item.Card
                   ?? throw new InvalidOperationException(
                       "CollectionItem.Card must be loaded before mapping.");

        return new(
            item.Id,
            CollectionCardResponse.From(card, placeholderImageUrl),
            item.Quantity,
            item.Condition,
            item.IsForSale,
            item.Price,
            item.Currency,
            item.IsPrivate,
            item.Notes,
            item.CreatedAt,
            item.UpdatedAt);
    }
}

/// <summary>
/// Display projection of the catalog <see cref="Card"/> a binder row points at. Same
/// never-null image guarantee as the catalog search DTO: a configured placeholder is
/// substituted and <see cref="HasImage"/> flags the fallback.
/// </summary>
public sealed record CollectionCardResponse(
    int Id,
    string ExternalId,
    string Name,
    string? Number,
    string? Rarity,
    string ImageUrl,
    bool HasImage,
    string? SetName)
{
    public static CollectionCardResponse From(Card card, string placeholderImageUrl)
    {
        var hasImage = !string.IsNullOrWhiteSpace(card.ImageUrl);
        return new(
            card.Id,
            card.ExternalId,
            card.Name,
            card.Number,
            card.Rarity,
            hasImage ? card.ImageUrl! : placeholderImageUrl,
            hasImage,
            card.CardSet?.Name);
    }
}
