namespace NbTcgTrader.Api.Common.Domain;

/// <summary>
/// A user's owned copies of a card — the binder row, and (when <see cref="IsForSale"/>
/// and not <see cref="IsPrivate"/>) the marketplace listing. CLAUDE.md §7 notes the
/// for-sale fields split into a dedicated Listing entity in phase 2.
/// </summary>
public sealed class CollectionItem
{
    public int Id { get; set; }

    public required string UserId { get; set; }

    public AppUser? User { get; set; }

    public int CardId { get; set; }

    public Card? Card { get; set; }

    public int Quantity { get; set; }

    public CardCondition Condition { get; set; }

    public bool IsForSale { get; set; }

    public decimal? Price { get; set; }

    public Currency Currency { get; set; }

    public bool IsPrivate { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
