using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Application.Features.Auth.DTOs;
using FluentAssertions;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

/// <summary>
/// Validation failures are rendered as one ProblemDetails extension per invalid field. This pins the
/// shape, because it is a client-visible contract.
/// </summary>
public class ValidationErrorShapeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ValidationErrorShapeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WithInvalidPayload_NamesEachInvalidField()
    {
        _factory.RequireContainers();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest("not-an-email", "short", "", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        root.GetProperty("title").GetString().Should().Be("ValidationFailure");
        root.GetProperty("detail").GetString().Should().Be("One or more validation errors occurred.");

        // Extension keys are written verbatim, so compare case-insensitively rather than guessing the
        // casing the serializer settled on.
        var propertyNames = root.EnumerateObject().Select(property => property.Name).ToList();
        propertyNames.Should().Contain(name => string.Equals(name, "Email", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().Contain(name => string.Equals(name, "Password", StringComparison.OrdinalIgnoreCase));

        var emailErrors = root.EnumerateObject()
            .First(property => string.Equals(property.Name, "Email", StringComparison.OrdinalIgnoreCase))
            .Value;

        emailErrors.ValueKind.Should().Be(JsonValueKind.Array);
        emailErrors.GetArrayLength().Should().BeGreaterThan(0);
    }
}
