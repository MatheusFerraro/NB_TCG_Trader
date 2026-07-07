using NbTcgTrader.Api.Features.Catalog;
using Shouldly;

namespace NbTcgTrader.Tests;

// Fast, DB-free validator checks for the catalog browse query (BACKLOG #9, CLAUDE.md §11).
public class CatalogSearchValidatorTests
{
    private static readonly CatalogSearchRequestValidator Validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var request = new CatalogSearchRequest(Query: "Charizard", Set: "base1", Number: "4",
            Page: 1, PageSize: 25);

        Validator.Validate(request).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_filterless_request()
    {
        // A filterless search maps to the provider's match-everything query (q=*),
        // which pokemontcg.io cannot answer — it burns the retry budget and 503s (#48).
        var result = Validator.Validate(new CatalogSearchRequest());

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CatalogSearchRequest.Query));
    }

    [Theory]
    [InlineData("charizard", null, null)]
    [InlineData(null, "base1", null)]
    [InlineData(null, null, "4")]
    public void Any_single_filter_passes(string? query, string? set, string? number)
    {
        var request = new CatalogSearchRequest(Query: query, Set: set, Number: number);

        Validator.Validate(request).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Whitespace_only_filters_are_rejected()
    {
        var result = Validator.Validate(new CatalogSearchRequest(Query: " ", Set: "\t", Number: " "));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_page_below_one(int page)
    {
        var result = Validator.Validate(new CatalogSearchRequest(Page: page));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CatalogSearchRequest.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Rejects_page_size_out_of_range(int pageSize)
    {
        var result = Validator.Validate(new CatalogSearchRequest(PageSize: pageSize));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CatalogSearchRequest.PageSize));
    }

    [Fact]
    public void Rejects_overlong_query()
    {
        var result = Validator.Validate(new CatalogSearchRequest(Query: new string('x', 101)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CatalogSearchRequest.Query));
    }
}
