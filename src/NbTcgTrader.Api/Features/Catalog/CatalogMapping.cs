using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// Maps the provider-agnostic catalog result records onto the domain entities
/// (CLAUDE.md §7). The catalog client stays free of EF concerns, so the owning
/// <c>GameId</c> (and the resolved <c>CardSetId</c>) are supplied by the persistence
/// slices that turn search results into stored <see cref="Card"/>/<see cref="CardSet"/>
/// rows (#9/#10). Returned entities are detached — the caller attaches/saves them.
/// </summary>
public static class CatalogMapping
{
    public static Card ToCard(this CatalogCard card, int gameId, int? cardSetId = null) => new()
    {
        GameId = gameId,
        CardSetId = cardSetId,
        ExternalId = card.ExternalId,
        Name = card.Name,
        Number = card.Number,
        Rarity = card.Rarity,
        ImageUrl = card.ImageUrl,
        Metadata = card.Metadata,
    };

    public static CardSet ToCardSet(this CatalogSet set, int gameId) => new()
    {
        GameId = gameId,
        Name = set.Name,
        Code = set.Code,
        ReleaseDate = set.ReleaseDate,
        ExternalId = set.ExternalId,
    };
}
