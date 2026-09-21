using ECommerce.Application.Features.Cart.Commands;
using FluentAssertions;
using Xunit;

namespace ECommerce.Application.UnitTests;

public class CartValidationTests
{
    private readonly AddItemToCartCommandValidator _addItemValidator = new();
    private readonly UpdateCartItemCommandValidator _updateItemValidator = new();

    [Theory]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void AddItemToCartCommandValidator_ValidatesQuantity(int quantity, bool expectedIsValid)
    {
        var command = new AddItemToCartCommand(Guid.NewGuid(), quantity);
        var result = _addItemValidator.Validate(command);
        result.IsValid.Should().Be(expectedIsValid);
    }

    [Theory]
    [InlineData(0, true)] // 0 means remove from cart
    [InlineData(5, true)]
    [InlineData(-1, false)] // negative is invalid
    public void UpdateCartItemCommandValidator_ValidatesQuantity(int quantity, bool expectedIsValid)
    {
        var command = new UpdateCartItemCommand(Guid.NewGuid(), quantity);
        var result = _updateItemValidator.Validate(command);
        result.IsValid.Should().Be(expectedIsValid);
    }
}
