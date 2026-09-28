using System.Net;
using System.Net.Http.Json;
using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Application.Features.Auth.DTOs;
using FluentAssertions;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

/// <summary>
/// Reuse of a rotated refresh token must revoke the whole token family, so a thief cannot keep
/// using a descendant. The grace window is 10 seconds, so this test waits it out.
/// </summary>
public class RefreshTokenReuseTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RefreshTokenReuseTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReusingARotatedToken_RevokesEveryTokenInTheFamily()
    {
        _factory.RequireContainers();

        var client = _factory.CreateClient();
        var email = $"reuse_{Guid.NewGuid():N}@test.com";
        const string password = "Password123!#";

        var registered = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(email, password, "Reuse", "Test"));
        registered.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();
        var first = await login.Content.ReadFromJsonAsync<AuthResponse>();
        first.Should().NotBeNull();

        // Rotate once: the original token is now revoked and replaced by this child.
        var rotation = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(first!.RefreshToken));
        rotation.EnsureSuccessStatusCode();
        var child = await rotation.Content.ReadFromJsonAsync<AuthResponse>();
        child.Should().NotBeNull();

        // Wait past the grace window, then reuse the rotated original.
        await Task.Delay(TimeSpan.FromSeconds(11));

        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(first.RefreshToken));
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "reuse beyond the grace window is rejected");

        // The child must be dead too. Without family revocation it would still rotate successfully,
        // which is exactly the hole this test exists to close.
        var childAfterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(child!.RefreshToken));
        childAfterReuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the whole family is revoked on reuse");
    }
}
