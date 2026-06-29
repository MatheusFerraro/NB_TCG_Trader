namespace NbTcgTrader.Api.Common.Domain;

/// <summary>A trading-card game (e.g. Pokémon, Magic). The schema is TCG-agnostic.</summary>
public sealed class Game
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>URL-friendly identifier, e.g. "pokemon".</summary>
    public required string Slug { get; set; }

    public ICollection<CardSet> CardSets { get; set; } = new List<CardSet>();

    public ICollection<Card> Cards { get; set; } = new List<Card>();
}
