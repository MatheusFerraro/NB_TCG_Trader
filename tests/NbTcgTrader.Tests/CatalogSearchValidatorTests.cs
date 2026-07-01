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
    public void Empty_request_passes()
    {
        // All filters optional; defaults (page 1, size 25) are valid, so bare browse works.
        Validator.Validate(new CatalogSearchRequest()).IsValid.ShouldBeTrue();
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
