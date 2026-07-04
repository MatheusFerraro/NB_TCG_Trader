using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// Shared reconciliation rules for the Import slice (BACKLOG #16): turning a matched
/// <see cref="ImportRow"/> into a binder <see cref="CollectionItem"/> and recomputing a
/// job's progress. Used by the upload path (auto-matched rows, #15) and the manual
/// resolve path so both materialize rows the same way — a matched row always becomes a
/// binder item, whether the match was automatic or hand-picked.
/// </summary>
internal static class ImportReconciliation
{
    /// <summary>
    /// Builds the binder row a matched import row implies, carrying over the quantity,
    /// condition, and listing fields the parser kept on the row (CLAUDE.md §9). Condition
    /// falls back to Near Mint like the add-to-binder flow. The parser already guarantees
    /// a <c>for_sale</c> row carries a price ("a listing needs an asking price"), so the
    /// flag is trusted here. Currency is left at its default (CAD), since the template has
    /// no currency column; the edit endpoint can change it.
    /// </summary>
    public static CollectionItem ToBinderItem(
        ImportRow row, Card card, string userId, DateTimeOffset now) =>
        new()
        {
            UserId = userId,
            Card = card,
            Quantity = row.Quantity,
            Condition = row.Condition ?? CardCondition.NM,
            IsForSale = row.IsForSale,
            Price = row.Price,
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>
    /// Recomputes a job's counts and status from its rows. Matched = auto- or
    /// manually-matched; skipped rows count as neither. A job is Completed once nothing is
    /// left Unmatched (BACKLOG #16 AC), otherwise it still NeedsReview.
    /// </summary>
    public static void RecomputeProgress(ImportJob job)
    {
        job.RowsMatched = job.Rows.Count(r =>
            r.MatchStatus is MatchStatus.AutoMatched or MatchStatus.ManuallyMatched);
        job.RowsUnmatched = job.Rows.Count(r => r.MatchStatus == MatchStatus.Unmatched);
        job.Status = job.RowsUnmatched == 0
            ? ImportStatus.Completed
            : ImportStatus.NeedsReview;
    }
}
