using FluentValidation;
using Microsoft.Extensions.Options;

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
/// Searches the external catalog and projects results into display DTOs, substituting the
/// configured placeholder for cards the provider returns without an image (never dropping
/// a card). The client handles caching, resilience, and page-size clamping.
/// </summary>
public sealed class SearchCardsHandler(
    ICardCatalogClient catalog,
    IOptions<CatalogOptions> options)
{
    public async Task<IResult> HandleAsync(CatalogSearchRequest request, CancellationToken cancellationToken)
    {
        var page = await catalog.SearchCardsAsync(
            new CatalogSearchQuery(
                request.Query,
                request.Set,
                request.Number,
                request.Page,
                request.PageSize),
            cancellationToken);

        var placeholder = options.Value.PlaceholderImageUrl;
        var items = page.Items
            .Select(card => CatalogCardResponse.From(card, placeholder))
            .ToList();

        return Results.Ok(new CatalogPage<CatalogCardResponse>(
            items, page.Page, page.PageSize, page.TotalCount));
    }
}
