namespace NbTcgTrader.Api.Common.Domain;

/// <summary>
/// A catalog entry shared across users (not owned by anyone). Populated from the
/// external card-data provider. Images are hotlinked via <see cref="ImageUrl"/>;
/// we never store the binary (CLAUDE.md §8).
/// </summary>
public sealed class Card
{
    public int Id { get; set; }

    public int GameId { get; set; }

    public Game? Game { get; set; }

    public int? CardSetId { get; set; }

    public CardSet? CardSet { get; set; }

    /// <summary>Identifier from the external card-data provider (e.g. pokemontcg.io id).</summary>
    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    /// <summary>Collector number within the set, e.g. "4/102".</summary>
    public string? Number { get; set; }

    public string? Rarity { get; set; }

    /// <summary>Remote image URL to hotlink; never a stored blob.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Variable per-TCG attributes, stored as Postgres <c>jsonb</c>.</summary>
    public string? Metadata { get; set; }

    public ICollection<CollectionItem> CollectionItems { get; set; } = new List<CollectionItem>();
}
