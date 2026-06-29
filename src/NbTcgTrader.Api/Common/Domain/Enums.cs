namespace NbTcgTrader.Api.Common.Domain;

/// <summary>Card grading/condition, worst-to-best left to right is not implied.</summary>
public enum CardCondition
{
    /// <summary>Near Mint.</summary>
    NM,

    /// <summary>Lightly Played.</summary>
    LP,

    /// <summary>Moderately Played.</summary>
    MP,

    /// <summary>Heavily Played.</summary>
    HP,

    /// <summary>Damaged.</summary>
    DMG
}

/// <summary>Currency a listing is priced in. Localized to our two markets.</summary>
public enum Currency
{
    /// <summary>Canadian dollar (Moncton, NB).</summary>
    CAD,

    /// <summary>Brazilian real (Ipaussu, SP).</summary>
    BRL
}

/// <summary>Lifecycle of a CSV/XLSX import.</summary>
public enum ImportStatus
{
    Pending,
    Processing,
    NeedsReview,
    Completed,
    Failed
}

/// <summary>How a parsed import row was reconciled against the catalog.</summary>
public enum MatchStatus
{
    Unmatched,
    AutoMatched,
    ManuallyMatched,
    Skipped
}
