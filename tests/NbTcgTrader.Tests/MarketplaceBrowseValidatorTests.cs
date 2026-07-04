using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Features.Marketplace;
using Shouldly;

namespace NbTcgTrader.Tests;

// Validator coverage for the marketplace browse query (BACKLOG #17). Pure unit tests —
// no host, network, or database.
public sealed class MarketplaceBrowseValidatorTests
{
    private readonly BrowseListingsRequestValidator _validator = new();

    [Fact]
    public void Defaults_pass()
    {
        _validator.Validate(new BrowseListingsRequest()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void All_filters_populated_pass()
    {
        var request = new BrowseListingsRequest(
            Game: "pokemon",
            Set: "Base",
            Name: "Charizard",
            MinPrice: 10m,
            MaxPrice: 50m,
            Currency: Currency.CAD,
            City: "Moncton",
            Country: "Canada",
            Page: 2,
            PageSize: 50);

        _validator.Validate(request).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(BrowseListingsRequestValidator.MaxPage + 1)]
    public void Out_of_range_page_fails(int page)
    {
        var result = _validator.Validate(new BrowseListingsRequest(Page: page));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(BrowseListingsRequest.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Out_of_range_page_size_fails(int pageSize)
    {
        var result = _validator.Validate(new BrowseListingsRequest(PageSize: pageSize));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(BrowseListingsRequest.PageSize));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Boundary_page_sizes_pass(int pageSize)
    {
        _validator.Validate(new BrowseListingsRequest(PageSize: pageSize)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Negative_min_price_fails()
    {
        var result = _validator.Validate(new BrowseListingsRequest(MinPrice: -1m));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(BrowseListingsRequest.MinPrice));
    }

    [Fact]
    public void Negative_max_price_fails()
    {
        var result = _validator.Validate(new BrowseListingsRequest(MaxPrice: -1m));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(BrowseListingsRequest.MaxPrice));
    }

    [Fact]
    public void Max_price_below_min_price_fails()
    {
        var result = _validator.Validate(
            new BrowseListingsRequest(MinPrice: 50m, MaxPrice: 10m));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(BrowseListingsRequest.MaxPrice));
    }

    [Fact]
    public void Equal_min_and_max_price_pass()
    {
        _validator.Validate(new BrowseListingsRequest(MinPrice: 25m, MaxPrice: 25m))
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Over_length_text_filter_fails()
    {
        var result = _validator.Validate(
            new BrowseListingsRequest(Name: new string('x', 101)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(BrowseListingsRequest.Name));
    }
}
