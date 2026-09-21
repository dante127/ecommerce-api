using ECommerce.Application.Features.Auth.Commands.Login;
using ECommerce.Application.Features.Auth.Commands.RefreshToken;
using ECommerce.Application.Features.Auth.Commands.Register;
using FluentAssertions;
using Xunit;

namespace ECommerce.Application.UnitTests;

public class AuthValidationTests
{
    private readonly RegisterCommandValidator _registerValidator = new();
    private readonly LoginCommandValidator _loginValidator = new();
    private readonly RefreshTokenCommandValidator _refreshTokenValidator = new();

    [Theory]
    [InlineData("valid@example.com", "Password123!#", "John", "Doe", true)]
    [InlineData("invalid-email", "Password123!#", "John", "Doe", false)]
    [InlineData("valid@example.com", "short", "John", "Doe", false)] // < 8 chars
    [InlineData("valid@example.com", "password123!", "John", "Doe", false)] // no uppercase
    [InlineData("valid@example.com", "PASSWORD123!", "John", "Doe", false)] // no lowercase
    [InlineData("valid@example.com", "Password!!!!", "John", "Doe", false)] // no digit
    [InlineData("valid@example.com", "Password1234", "John", "Doe", false)] // no special char
    [InlineData("valid@example.com", "Password123!#", "", "Doe", false)] // empty first name
    [InlineData("valid@example.com", "Password123!#", "John", "", false)] // empty last name
    public void RegisterCommandValidator_ValidatesRulesProperly(
        string email, string password, string firstName, string lastName, bool expectedIsValid)
    {
        // Arrange
        var command = new RegisterCommand(email, password, firstName, lastName);

        // Act
        var result = _registerValidator.Validate(command);

        // Assert
        result.IsValid.Should().Be(expectedIsValid);
    }

    [Theory]
    [InlineData("user@example.com", "AnyPassword", true)]
    [InlineData("invalid-email", "AnyPassword", false)]
    [InlineData("", "AnyPassword", false)]
    [InlineData("user@example.com", "", false)]
    public void LoginCommandValidator_ValidatesRulesProperly(string email, string password, bool expectedIsValid)
    {
        // Arrange
        var command = new LoginCommand(email, password);

        // Act
        var result = _loginValidator.Validate(command);

        // Assert
        result.IsValid.Should().Be(expectedIsValid);
    }

    [Theory]
    [InlineData("valid-raw-refresh-token", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void RefreshTokenCommandValidator_ValidatesRulesProperly(string token, bool expectedIsValid)
    {
        // Arrange
        var command = new RefreshTokenCommand(token);

        // Act
        var result = _refreshTokenValidator.Validate(command);

        // Assert
        result.IsValid.Should().Be(expectedIsValid);
    }
}
