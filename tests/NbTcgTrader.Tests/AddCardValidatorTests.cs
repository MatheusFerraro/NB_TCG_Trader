using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Features.Collection;
using Shouldly;

namespace NbTcgTrader.Tests;

// Validator coverage for the add-to-collection request (BACKLOG #10). Pure unit
// tests — no host, network, or database.
public sealed class AddCardValidatorTests
{
    private readonly AddCardValidator _validator = new();

    private static AddCardRequest Valid() => new("base1-4", Quantity: 1, CardCondition.NM);

    [Fact]
    public void Valid_request_passes()
    {
        _validator.Validate(Valid()).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_card_external_id_fails(string externalId)
    {
        var result = _validator.Validate(Valid() with { CardExternalId = externalId });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(AddCardRequest.CardExternalId));
    }

    [Fact]
    public void Card_external_id_longer_than_column_fails()
    {
        var result = _validator.Validate(Valid() with { CardExternalId = new string('x', 101) });

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void Out_of_range_quantity_fails(int quantity)
    {
        var result = _validator.Validate(Valid() with { Quantity = quantity });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(AddCardRequest.Quantity));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    public void Boundary_quantities_pass(int quantity)
    {
        _validator.Validate(Valid() with { Quantity = quantity }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Undefined_condition_fails()
    {
        var result = _validator.Validate(Valid() with { Condition = (CardCondition)42 });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(AddCardRequest.Condition));
    }
}
