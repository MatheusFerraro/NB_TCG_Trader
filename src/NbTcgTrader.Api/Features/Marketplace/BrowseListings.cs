using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Marketplace;

/// <summary>
/// Query for the public marketplace browse endpoint (BACKLOG #17), bound from the query
/// string via <c>[AsParameters]</c>. Every filter is optional and they combine (AND). A
/// price range is only coherent within one currency, so <see cref="Currency"/> narrows it
/// — filtering by <see cref="MinPrice"/>/<see cref="MaxPrice"/> across mixed CAD/BRL
/// listings is otherwise meaningless.
/// </summary>
public sealed record BrowseListingsRequest(
    string? Game = null,
    string? Set = null,
    string? Name = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    Currency? Currency = null,
    string? City = null,
    string? Country = null,
    int Page = 1,
    int PageSize = 25);

public sealed class BrowseListingsRequestValidator : AbstractValidator<BrowseListingsRequest>
{
    public const int MaxPage = 10_000;

    public BrowseListingsRequestValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, MaxPage);

        // Same hard cap as the binder/catalog browse pages; bounds the query's joins.
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);

        RuleFor(x => x.Game).MaximumLength(100);
        RuleFor(x => x.Set).MaximumLength(100);
        RuleFor(x => x.Name).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);

        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0).When(x => x.MinPrice.HasValue);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0).When(x => x.MaxPrice.HasValue);

        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(x => x.MinPrice!.Value)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue)
            .WithMessage("'Max Price' must be greater than or equal to 'Min Price'.");
    }
}

/// <summary>
/// Returns one page of public for-sale listings (BACKLOG #17), newest first. The base
/// filter <c>IsForSale &amp;&amp; !IsPrivate</c> is enforced server-side and can never be
/// widened by the caller, so a private or not-for-sale item never surfaces (CLAUDE.md §15).
/// Each row carries the card display data and the seller's public location, so the grid
/// needs no follow-up joins. Anonymous: browsing exposes only public listing data.
/// </summary>
public sealed class BrowseListingsHandler(
    AppDbContext db,
    IOptions<CatalogOptions> catalogOptions)
{
    public async Task<IResult> HandleAsync(
        BrowseListingsRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.CollectionItems
            .AsNoTracking()
            .Where(i => i.IsForSale && !i.IsPrivate);

        if (!string.IsNullOrWhiteSpace(request.Game))
        {
            var game = request.Game.Trim();
            query = query.Where(i => i.Card!.Game!.Slug.ToLower() == game.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(request.Set))
        {
            var set = $"%{Escape(request.Set.Trim())}%";
            query = query.Where(i =>
                i.Card!.CardSet != null &&
                (EF.Functions.ILike(i.Card.CardSet.Name, set) ||
                 EF.Functions.ILike(i.Card.CardSet.Code, set)));
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = $"%{Escape(request.Name.Trim())}%";
            query = query.Where(i => EF.Functions.ILike(i.Card!.Name, name));
        }

        if (request.MinPrice.HasValue)
        {
            query = query.Where(i => i.Price >= request.MinPrice.Value);
        }

        if (request.MaxPrice.HasValue)
        {
            query = query.Where(i => i.Price <= request.MaxPrice.Value);
        }

        if (request.Currency.HasValue)
        {
            query = query.Where(i => i.Currency == request.Currency.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var city = request.City.Trim();
            query = query.Where(i =>
                i.User!.City != null && i.User.City.ToLower() == city.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            var country = request.Country.Trim();
            query = query.Where(i =>
                i.User!.Country != null && i.User.Country.ToLower() == country.ToLower());
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var placeholder = catalogOptions.Value.PlaceholderImageUrl;
        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id) // tie-break so paging is deterministic
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(i => new ListingRow(
                i.Id,
                i.Card!.Id,
                i.Card.ExternalId,
                i.Card.Name,
                i.Card.Number,
                i.Card.Rarity,
                i.Card.ImageUrl,
                i.Card.CardSet == null ? null : i.Card.CardSet.Name,
                i.Card.Game!.Name,
                i.Quantity,
                i.Condition,
                i.Price,
                i.Currency,
                i.Notes,
                i.User!.DisplayName,
                i.User.City,
                i.User.Country,
                i.CreatedAt,
                i.UpdatedAt))
            .ToListAsync(cancellationToken);

        var responses = rows
            .Select(r => r.ToResponse(placeholder))
            .ToList();

        return Results.Ok(new CatalogPage<MarketplaceListingResponse>(
            responses, request.Page, request.PageSize, totalCount));
    }

    // ILike treats % and _ as wildcards; escape any the user typed so a filter like
    // "50%" matches literally instead of "starts with 50". The parameter itself is still
    // parameterized by EF — this only fixes the match semantics, not injection.
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private sealed record ListingRow(
        int Id,
        int CardId,
        string CardExternalId,
        string CardName,
        string? CardNumber,
        string? CardRarity,
        string? CardImageUrl,
        string? SetName,
        string GameName,
        int Quantity,
        CardCondition Condition,
        decimal? Price,
        Currency Currency,
        string? Notes,
        string SellerDisplayName,
        string? SellerCity,
        string? SellerCountry,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt)
    {
        public MarketplaceListingResponse ToResponse(string placeholderImageUrl)
        {
            var hasImage = !string.IsNullOrWhiteSpace(CardImageUrl);
            return new MarketplaceListingResponse(
                Id,
                new MarketplaceCardResponse(
                    CardId,
                    CardExternalId,
                    CardName,
                    CardNumber,
                    CardRarity,
                    hasImage ? CardImageUrl! : placeholderImageUrl,
                    hasImage,
                    SetName,
                    GameName),
                Quantity,
                Condition,
                Price,
                Currency,
                Notes,
                new MarketplaceSellerResponse(
                    SellerDisplayName,
                    SellerCity,
                    SellerCountry),
                CreatedAt,
                UpdatedAt);
        }
    }
}
