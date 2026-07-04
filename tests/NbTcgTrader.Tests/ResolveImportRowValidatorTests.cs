using NbTcgTrader.Api.Features.Import;
using Shouldly;

namespace NbTcgTrader.Tests;

// Validator coverage for manually resolving an import row (BACKLOG #16). Pure unit
// tests: the endpoint filter should reject bad card ids before catalog lookup.
public sealed class ResolveImportRowValidatorTests
{
    private readonly ResolveImportRowValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var result = _validator.Validate(new ResolveImportRowRequest("base1-4"));

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_card_external_id_fails(string externalId)
    {
        var result = _validator.Validate(new ResolveImportRowRequest(externalId));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.PropertyName == nameof(ResolveImportRowRequest.CardExternalId));
    }

    [Fact]
    public void Card_external_id_longer_than_column_fails()
    {
        var result = _validator.Validate(new ResolveImportRowRequest(new string('x', 101)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.PropertyName == nameof(ResolveImportRowRequest.CardExternalId));
    }
}
