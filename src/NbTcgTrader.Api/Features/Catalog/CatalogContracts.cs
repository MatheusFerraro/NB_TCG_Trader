namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// Display projection of a <see cref="CatalogCard"/> for the "browse and add" flow
/// (BACKLOG #9). <see cref="ImageUrl"/> is never null: when the provider has no image,
/// a configured placeholder is substituted and <see cref="HasImage"/> is set to false so
/// the client can distinguish a real image from the fallback. Provider <c>Metadata</c> is
/// intentionally omitted — it is not a display field.
/// </summary>
public sealed record CatalogCardResponse(
    string ExternalId,
    string Name,
    string? Number,
    string? Rarity,
    string ImageUrl,
    bool HasImage,
    CatalogSetResponse? Set)
{
    public static CatalogCardResponse From(CatalogCard card, string placeholderUrl)
    {
        var hasImage = !string.IsNullOrWhiteSpace(card.ImageUrl);
        return new(
            card.ExternalId,
            card.Name,
            card.Number,
            card.Rarity,
            hasImage ? card.ImageUrl! : placeholderUrl,
            hasImage,
            card.Set is null ? null : CatalogSetResponse.From(card.Set));
    }
}

/// <summary>Display projection of a <see cref="CatalogSet"/>.</summary>
public sealed record CatalogSetResponse(
    string ExternalId,
    string Name,
    string Code,
    DateOnly? ReleaseDate)
{
    public static CatalogSetResponse From(CatalogSet set) =>
        new(set.ExternalId, set.Name, set.Code, set.ReleaseDate);
}
