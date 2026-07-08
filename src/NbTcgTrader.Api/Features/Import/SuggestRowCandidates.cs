using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// A ranked catalog suggestion for an unmatched import row (#66 follow-up). Carries the
/// card's <c>ExternalId</c> so the review screen can resolve the row directly (§9 /
/// <see cref="ResolveImportRowHandler"/>) without a second catalog search. Set name/code
/// disambiguate reprints (e.g. a Charizard printed in two sets). <see cref="ImageUrl"/> is
/// never null: the configured placeholder is substituted when the card has no image.
/// </summary>
public sealed record ImportRowCandidateResponse(
    string ExternalId,
    string Name,
    string? Number,
    string? Rarity,
    string ImageUrl,
    bool HasImage,
    string? SetName,
    string? SetCode);

/// <summary>The ranked suggestions for one row, echoing the raw name they were ranked against.</summary>
public sealed record ImportRowCandidatesResponse(
    int RowId,
    string RawName,
    IReadOnlyList<ImportRowCandidateResponse> Candidates);

/// <summary>
/// Suggests the most likely catalog cards for an import row (#66 follow-up: import-assist).
/// Auto-matching (#15) only ever commits an exact single hit and leaves everything else
/// <see cref="MatchStatus.Unmatched"/>; this ranks the <b>local</b> catalog by pg_trgm name
/// similarity so the manual reconcile screen can offer "did you mean…" candidates instead of
/// making the user search from scratch. Fully local — no external provider call — and every
/// suggestion is a real <c>Card</c> from the catalog (never invented). Owner-scoped: another
/// user's job (or a missing one) is an indistinguishable 404 (CLAUDE.md §15).
/// </summary>
public sealed class SuggestRowCandidatesHandler(
    AppDbContext db,
    IOptions<CatalogOptions> options)
{
    // The MVP catalog is Pokémon (CLAUDE.md §8); scope suggestions to that game.
    private const string GameSlug = "pokemon";

    // A short "did you mean" list; the user still confirms one, so a handful is enough.
    private const int MaxCandidates = 5;

    public async Task<IResult> HandleAsync(
        int jobId,
        int rowId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        // Owner-scoped: another user's job (or a missing one) is an indistinguishable 404.
        // Check ownership cheaply, then fetch just the one row — a large import can hold
        // thousands of rows and none but this one is needed here.
        var jobExists = await db.ImportJobs
            .AsNoTracking()
            .AnyAsync(j => j.Id == jobId && j.UserId == userId, cancellationToken);

        if (!jobExists)
        {
            return ListImportRowsHandler.NotFound(jobId);
        }

        var row = await db.ImportRows
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rowId && r.ImportJobId == jobId, cancellationToken);

        if (row is null)
        {
            return RowNotFound(jobId, rowId);
        }

        var candidates = await RankCandidatesAsync(row, cancellationToken);
        return Results.Ok(new ImportRowCandidatesResponse(row.Id, row.RawName, candidates));
    }

    private async Task<IReadOnlyList<ImportRowCandidateResponse>> RankCandidatesAsync(
        ImportRow row, CancellationToken cancellationToken)
    {
        var term = row.RawName.Trim();
        if (term.Length == 0)
        {
            return [];
        }

        // The `%` operator (TrigramsAreSimilar) filters to names above pg_trgm's similarity
        // threshold, catching typos ILIKE '%term%' would miss ("charzard" → "Charizard"),
        // and it rides the same GIN index. similarity() then ranks the survivors best-first.
        var query = db.Cards
            .AsNoTracking()
            .Include(c => c.CardSet)
            .Where(c => c.Game!.Slug == GameSlug)
            .Where(c => EF.Functions.TrigramsAreSimilar(c.Name, term));

        // When the row named a collector number, prefer the printing that carries it before
        // falling back to name similarity — the user told us which one they mean.
        var number = row.RawNumber?.Trim();
        var ordered = string.IsNullOrWhiteSpace(number)
            ? query.OrderByDescending(c => EF.Functions.TrigramsSimilarity(c.Name, term))
            : query
                .OrderByDescending(c => c.Number == number)
                .ThenByDescending(c => EF.Functions.TrigramsSimilarity(c.Name, term));

        var cards = await ordered
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Take(MaxCandidates)
            .ToListAsync(cancellationToken);

        var placeholder = options.Value.PlaceholderImageUrl;
        return cards.Select(c => ToCandidate(c, placeholder)).ToList();
    }

    private static ImportRowCandidateResponse ToCandidate(Card card, string placeholderUrl)
    {
        var hasImage = !string.IsNullOrWhiteSpace(card.ImageUrl);
        return new ImportRowCandidateResponse(
            card.ExternalId,
            card.Name,
            card.Number,
            card.Rarity,
            hasImage ? card.ImageUrl! : placeholderUrl,
            hasImage,
            card.CardSet?.Name,
            card.CardSet?.Code);
    }

    private static IResult RowNotFound(int jobId, int rowId) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Import row not found",
        detail: $"No row {rowId} exists on import job {jobId} for you.");
}
