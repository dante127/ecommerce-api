using ECommerce.Application.Features.Orders.Commands;
using ECommerce.Application.Features.Orders.DTOs;
using FluentAssertions;
using Xunit;

namespace ECommerce.Application.UnitTests;

public class OrderValidationTests
{
    private readonly CheckoutCommandValidator _checkoutValidator = new();

    [Fact]
    public void CheckoutCommandValidator_NullAddress_ShouldBeInvalid()
    {
        var command = new CheckoutCommand(null!);
        var result = _checkoutValidator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ShippingAddress");
    }

    [Theory]
    [InlineData("", "Amman", "Amman", "11118", "Jordan", false)]
    [InlineData("Wasfi Al-Tal", "", "Amman", "11118", "Jordan", false)]
    [InlineData("Wasfi Al-Tal", "Amman", "", "11118", "Jordan", false)]
    [InlineData("Wasfi Al-Tal", "Amman", "Amman", "", "Jordan", false)]
    [InlineData("Wasfi Al-Tal", "Amman", "Amman", "11118", "", false)]
    [InlineData("Wasfi Al-Tal", "Amman", "Amman", "11118", "Jordan", true)]
    public void CheckoutCommandValidator_ValidatesAddressFields(
        string street, string city, string state, string postalCode, string country, bool expectedIsValid)
    {
        var address = new AddressDto(street, city, state, postalCode, country);
        var command = new CheckoutCommand(address);
        var result = _checkoutValidator.Validate(command);
        result.IsValid.Should().Be(expectedIsValid);
    }
}
