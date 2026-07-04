using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// Applies the AGENTS.md §9 matching strategy to a job's rows (BACKLOG #15):
/// set + card_number present → exact catalog match; otherwise a name search that
/// auto-matches only when the catalog returns exactly one card. Anything ambiguous
/// or unknown stays <see cref="MatchStatus.Unmatched"/> for manual reconciliation
/// (#16) — never guessed. Matched provider cards are staged locally through the
/// shared <see cref="CatalogCardStore"/>, identical lookups within a job hit the
/// provider once, and a provider failure leaves the remaining rows Unmatched (the
/// job lands in NeedsReview and nothing is lost) rather than failing the upload.
/// Finally the job's counts and status (Completed / NeedsReview) are set.
/// </summary>
public sealed class ImportRowMatcher(
    ICardCatalogClient catalog,
    CatalogCardStore cardStore,
    ILogger<ImportRowMatcher> logger)
{
    public async Task MatchAsync(ImportJob job, CancellationToken cancellationToken)
    {
        // One provider lookup per distinct query within the job; null = no unique match.
        var resolved = new Dictionary<string, Card?>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in job.Rows.Where(r => r.MatchStatus == MatchStatus.Unmatched))
        {
            var bySetAndNumber = row.RawSet is not null && row.RawNumber is not null;
            var key = bySetAndNumber
                ? $"set:{row.RawSet}|number:{row.RawNumber}"
                : $"name:{row.RawName}";

            if (!resolved.TryGetValue(key, out var card))
            {
                try
                {
                    card = await ResolveAsync(row, bySetAndNumber, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex,
                        "Catalog lookup failed while matching import job for user {UserId}; " +
                        "remaining rows stay Unmatched for manual review",
                        job.UserId);
                    break;
                }

                resolved[key] = card;
            }

            if (card is not null)
            {
                row.MatchedCard = card;
                row.MatchStatus = MatchStatus.AutoMatched;
            }
        }

        // Shared with manual reconciliation (#16) so both paths compute counts + status
        // identically; at this point no row is ManuallyMatched yet.
        ImportReconciliation.RecomputeProgress(job);
    }

    /// <summary>
    /// Resolves one row against the catalog; null when the match is not exactly one
    /// card. PageSize 2 is enough: TotalCount carries the real hit count and only a
    /// single-hit page is ever consumed.
    /// </summary>
    private async Task<Card?> ResolveAsync(
        ImportRow row, bool bySetAndNumber, CancellationToken cancellationToken)
    {
        var query = bySetAndNumber
            ? new CatalogSearchQuery(Set: row.RawSet, Number: row.RawNumber, PageSize: 2)
            : new CatalogSearchQuery(Name: row.RawName, PageSize: 2);

        var page = await catalog.SearchCardsAsync(query, cancellationToken);
        if (page.TotalCount != 1 || page.Items.Count == 0)
        {
            return null;
        }

        var hit = page.Items[0];
        return await cardStore.FindCardAsync(hit.ExternalId, cancellationToken)
               ?? await cardStore.StageCardAsync(hit, cancellationToken);
    }
}
