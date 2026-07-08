using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// Query for the catalog browse endpoint (BACKLOG #9), bound from the query string via
/// <c>[AsParameters]</c>. <see cref="Query"/> is the free-text name filter; it maps onto
/// the client's <see cref="CatalogSearchQuery.Name"/>. Filters combine; at least one of
/// name/set/number is required (a filterless search cannot be answered by the provider, #48).
/// </summary>
public sealed record CatalogSearchRequest(
    string? Query = null,
    string? Set = null,
    string? Number = null,
    int Page = 1,
    int PageSize = 25);

public sealed class CatalogSearchRequestValidator : AbstractValidator<CatalogSearchRequest>
{
    public CatalogSearchRequestValidator()
    {
        // At least one filter is required: a filterless search maps to the provider's
        // match-everything query (q=*), which pokemontcg.io cannot answer within the
        // resilience pipeline's budget — measured 16s of retries ending in a 503 (#48).
        // The frontend already refuses to submit an empty search; fail fast here too.
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Query)
                       || !string.IsNullOrWhiteSpace(x.Set)
                       || !string.IsNullOrWhiteSpace(x.Number))
            .OverridePropertyName(nameof(CatalogSearchRequest.Query))
            .WithMessage("Provide at least one filter: a card name, set, or number.");

        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);

        // A sane hard cap; the client further clamps to the configured CardApi:MaxPageSize.
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);

        RuleFor(x => x.Query).MaximumLength(100);
        RuleFor(x => x.Set).MaximumLength(100);
        RuleFor(x => x.Number).MaximumLength(50);
    }
}

/// <summary>
/// Searches the local catalog in Postgres and projects results into display DTOs,
/// substituting the configured placeholder for cards without an image (never dropping a
/// card). The catalog is owned locally (seeded from pokemon-tcg-data, #72), so the hot
/// path no longer touches the external provider — fuzzy name search runs against a
/// pg_trgm index. The provider client stays for by-id fallback and import matching.
/// </summary>
public sealed class SearchCardsHandler(
    AppDbContext db,
    IOptions<CatalogOptions> options)
{
    // The MVP catalog is Pokémon (CLAUDE.md §8); scope the search to that game so the
    // schema can hold other TCGs without leaking them into this Pokémon-only browse.
    private const string GameSlug = "pokemon";

    public async Task<IResult> HandleAsync(CatalogSearchRequest request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.Cards
            .AsNoTracking()
            .Include(c => c.CardSet)
            .Where(c => c.Game!.Slug == GameSlug);

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            // Partial + fuzzy name match: ILIKE '%term%' is served by the pg_trgm GIN
            // index on Card.Name, so "chariz" finds "Charizard" without a full scan.
            var pattern = $"%{EscapeLikePattern(request.Query.Trim())}%";
            query = query.Where(c => EF.Functions.ILike(c.Name, pattern, LikeEscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(request.Set))
        {
            // Accept a provider set id ("base1"), a PTCGO code ("BS"), or a set name.
            var set = request.Set.Trim();
            var pattern = $"%{EscapeLikePattern(set)}%";
            query = query.Where(c => c.CardSet != null
                && (c.CardSet.ExternalId == set
                    || EF.Functions.ILike(c.CardSet.Code, pattern, LikeEscapeChar)
                    || EF.Functions.ILike(c.CardSet.Name, pattern, LikeEscapeChar)));
        }

        if (!string.IsNullOrWhiteSpace(request.Number))
        {
            var number = request.Number.Trim();
            query = query.Where(c => c.Number == number);
        }

        // totalCount is computed before paging so the client's pager stays correct.
        var totalCount = await query.CountAsync(cancellationToken);

        var cards = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var placeholder = options.Value.PlaceholderImageUrl;
        var items = cards.Select(c => ToResponse(c, placeholder)).ToList();

        return Results.Ok(new CatalogPage<CatalogCardResponse>(items, page, pageSize, totalCount));
    }

    /// <summary>Escape char for the ILIKE patterns, so user input can't inject wildcards.</summary>
    private const string LikeEscapeChar = "\\";

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    private static CatalogCardResponse ToResponse(Card card, string placeholderUrl)
    {
        var hasImage = !string.IsNullOrWhiteSpace(card.ImageUrl);
        return new CatalogCardResponse(
            card.ExternalId,
            card.Name,
            card.Number,
            card.Rarity,
            hasImage ? card.ImageUrl! : placeholderUrl,
            hasImage,
            card.CardSet is null
                ? null
                : new CatalogSetResponse(
                    card.CardSet.ExternalId ?? card.CardSet.Code,
                    card.CardSet.Name,
                    card.CardSet.Code,
                    card.CardSet.ReleaseDate));
    }
}
