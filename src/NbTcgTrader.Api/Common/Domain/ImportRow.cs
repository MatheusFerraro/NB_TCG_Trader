namespace NbTcgTrader.Api.Common.Domain;

/// <summary>
/// A single parsed row from an import, retained for reconciliation. Raw values are
/// kept verbatim so the user can resolve unmatched rows by hand (CLAUDE.md §9).
/// </summary>
public sealed class ImportRow
{
    public int Id { get; set; }

    public int ImportJobId { get; set; }

    public ImportJob? ImportJob { get; set; }

    public required string RawName { get; set; }

    public string? RawSet { get; set; }

    public string? RawNumber { get; set; }

    public int Quantity { get; set; }

    public decimal? Price { get; set; }

    public CardCondition? Condition { get; set; }

    public MatchStatus MatchStatus { get; set; }

    public int? MatchedCardId { get; set; }

    public Card? MatchedCard { get; set; }
}
