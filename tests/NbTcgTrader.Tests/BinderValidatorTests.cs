using NbTcgTrader.Api.Features.Collection;
using Shouldly;

namespace NbTcgTrader.Tests;

// Validator coverage for the binder view query (BACKLOG #11). Pure unit tests —
// no host, network, or database.
public sealed class BinderValidatorTests
{
    private readonly BinderRequestValidator _validator = new();

    [Fact]
    public void Defaults_pass()
    {
        _validator.Validate(new BinderRequest()).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_below_one_fails(int page)
    {
        var result = _validator.Validate(new BinderRequest(Page: page));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(BinderRequest.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Out_of_range_page_size_fails(int pageSize)
    {
        var result = _validator.Validate(new BinderRequest(PageSize: pageSize));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(BinderRequest.PageSize));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Boundary_page_sizes_pass(int pageSize)
    {
        _validator.Validate(new BinderRequest(PageSize: pageSize)).IsValid.ShouldBeTrue();
    }
}
