using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// A job's reconciliation progress, returned by every reconcile endpoint (BACKLOG #16)
/// so the review screen can update its summary and tell when the job is done. Enums
/// serialize as strings ("NeedsReview", "Completed") via the global JSON options.
/// </summary>
public sealed record ImportJobProgressResponse(
    int JobId,
    ImportStatus Status,
    int RowsTotal,
    int RowsMatched,
    int RowsUnmatched)
{
    public static ImportJobProgressResponse From(ImportJob job) => new(
        job.Id,
        job.Status,
        job.RowsTotal,
        job.RowsMatched,
        job.RowsUnmatched);
}

/// <summary>
/// A single row awaiting reconciliation, with the raw values kept verbatim from the
/// upload so the user can identify the card and pick the right catalog entry (§9). The
/// user searches the catalog (<c>GET /catalog</c>) and resolves the row by that card's
/// <c>externalId</c>.
/// </summary>
public sealed record UnmatchedRowResponse(
    int Id,
    string RawName,
    string? RawSet,
    string? RawNumber,
    int Quantity,
    decimal? Price,
    CardCondition? Condition,
    bool IsForSale)
{
    public static UnmatchedRowResponse From(ImportRow row) => new(
        row.Id,
        row.RawName,
        row.RawSet,
        row.RawNumber,
        row.Quantity,
        row.Price,
        row.Condition,
        row.IsForSale);
}

/// <summary>The rows still needing a decision, plus the job's current progress.</summary>
public sealed record UnmatchedRowsResponse(
    ImportJobProgressResponse Job,
    IReadOnlyList<UnmatchedRowResponse> Rows);

/// <summary>
/// The outcome of resolving one row: which binder item it created, the row's new status,
/// and the job progress after the resolve (so the caller sees when the job completes).
/// </summary>
public sealed record ResolveImportRowResponse(
    ImportJobProgressResponse Job,
    int RowId,
    MatchStatus MatchStatus,
    int CreatedItemId);

/// <summary>The outcome of skipping one row: its new status and the job progress after.</summary>
public sealed record SkipImportRowResponse(
    ImportJobProgressResponse Job,
    int RowId,
    MatchStatus MatchStatus);
