using System.Net;
using System.Net.Http.Json;
using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Application.Features.Auth.DTOs;
using FluentAssertions;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

public class RefreshTokenRotationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RefreshTokenRotationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RefreshTokenRotation_WithinGracePeriod_AllowsParallelRequests()
    {
        _factory.RequireContainers();

        var client = _factory.CreateClient();
        var email = $"refresh_grace_{Guid.NewGuid():N}@test.com";
        var password = "Password123!#";

        // Register user
        var regResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(
            email, password, "Grace", "Period"));
        regResponse.EnsureSuccessStatusCode();

        // Login to get initial tokens
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        auth.Should().NotBeNull();
        var originalRefreshToken = auth!.RefreshToken;

        // Perform parallel refresh requests with the same refresh token
        var refreshRequest = new RefreshTokenRequest(originalRefreshToken);
        var task1 = client.PostAsJsonAsync("/api/v1/auth/refresh", refreshRequest);
        var task2 = client.PostAsJsonAsync("/api/v1/auth/refresh", refreshRequest);

        var responses = await Task.WhenAll(task1, task2);

        // Both requests in parallel within the 10-second grace window should succeed
        responses[0].StatusCode.Should().Be(HttpStatusCode.OK);
        responses[1].StatusCode.Should().Be(HttpStatusCode.OK);

        var result1 = await responses[0].Content.ReadFromJsonAsync<AuthResponse>();
        var result2 = await responses[1].Content.ReadFromJsonAsync<AuthResponse>();

        result1!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result2!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }
}
