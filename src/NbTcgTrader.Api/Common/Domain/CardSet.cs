namespace NbTcgTrader.Api.Common.Domain;

/// <summary>An expansion/set within a game (e.g. "Base Set", "Scarlet &amp; Violet").</summary>
public sealed class CardSet
{
    public int Id { get; set; }

    public int GameId { get; set; }

    public Game? Game { get; set; }

    public required string Name { get; set; }

    /// <summary>Short set code, e.g. "base1".</summary>
    public required string Code { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    /// <summary>Identifier from the external card-data provider.</summary>
    public string? ExternalId { get; set; }

    public ICollection<Card> Cards { get; set; } = new List<Card>();
}
