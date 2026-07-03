using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Features.Collection;
using Shouldly;

namespace NbTcgTrader.Tests;

// Validator coverage for the edit-collection-item request (BACKLOG #12), including
// the soft rule "price required if for_sale". Pure unit tests — no host, network,
// or database.
public sealed class UpdateItemValidatorTests
{
    private readonly UpdateItemValidator _validator = new();

    private static UpdateItemRequest Valid() => new(
        Quantity: 1,
        Condition: CardCondition.NM,
        IsForSale: false,
        Price: null,
        Currency: Currency.CAD,
        IsPrivate: false,
        Notes: null);

    [Fact]
    public void Valid_request_passes()
    {
        _validator.Validate(Valid()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void For_sale_with_price_passes()
    {
        var request = Valid() with { IsForSale = true, Price = 12.50m };

        _validator.Validate(request).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void For_sale_without_price_fails()
    {
        var result = _validator.Validate(Valid() with { IsForSale = true, Price = null });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(UpdateItemRequest.Price));
    }

    [Fact]
    public void Price_without_for_sale_is_allowed()
    {
        var request = Valid() with { IsForSale = false, Price = 5m };

        _validator.Validate(request).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-9.99)]
    public void Non_positive_price_fails(decimal price)
    {
        var result = _validator.Validate(Valid() with { Price = price });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(UpdateItemRequest.Price));
    }

    [Fact]
    public void Price_with_more_than_two_decimals_fails()
    {
        var result = _validator.Validate(Valid() with { Price = 9.999m });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(UpdateItemRequest.Price));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void Out_of_range_quantity_fails(int quantity)
    {
        var result = _validator.Validate(Valid() with { Quantity = quantity });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(UpdateItemRequest.Quantity));
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
        result.Errors.ShouldContain(e => e.PropertyName == nameof(UpdateItemRequest.Condition));
    }

    [Fact]
    public void Undefined_currency_fails()
    {
        var result = _validator.Validate(Valid() with { Currency = (Currency)42 });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(UpdateItemRequest.Currency));
    }

    [Fact]
    public void Notes_longer_than_column_fails()
    {
        var result = _validator.Validate(Valid() with { Notes = new string('x', 1001) });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(UpdateItemRequest.Notes));
    }
}
