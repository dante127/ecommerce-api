using System.Net;
using System.Net.Http.Json;
using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Application.Features.Auth.DTOs;
using ECommerce.Domain.Entities;
using ECommerce.Domain.ValueObjects;
using ECommerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

/// <summary>
/// Authorization and account-security behaviors that no test covered: cross-user order access is
/// forbidden (IDOR), repeated failed sign-ins lock the account even for the correct password, and
/// a revoked refresh token cannot rotate.
/// </summary>
public class AuthSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid UserId)> RegisterAsync(string email)
    {
        var client = _factory.CreateClient();
        const string password = "Password123!#";
        (await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Auth", "Security"))).EnsureSuccessStatusCode();
        var authed = await _factory.CreateAuthenticatedClientAsync(email, password);

        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();
        }

        return (authed, userId);
    }

    [Fact]
    public async Task Customer_CannotReadAnotherCustomersOrder()
    {
        _factory.RequireContainers();

        var ownerEmail = $"idor_owner_{Guid.NewGuid():N}@test.com";
        var (ownerClient, ownerId) = await RegisterAsync(ownerEmail);

        Guid orderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.AsNoTracking().FirstAsync();
            var order = Order.Create(ownerId, new Address("Street 5", "City", "State", "12345", "Country"),
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(35),
                new[] { (product.Id, product.Name, product.Price, 1) });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        var attackerEmail = $"idor_attacker_{Guid.NewGuid():N}@test.com";
        await RegisterAsync(attackerEmail);

        var attackerClient = await _factory.CreateAuthenticatedClientAsync(attackerEmail, "Password123!#");
        var response = await attackerClient.GetAsync($"/api/v1/orders/{orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "one customer's order must be invisible to another");
    }

    [Fact]
    public async Task RepeatedFailedSignins_LockTheAccount_EvenForTheCorrectPassword()
    {
        _factory.RequireContainers();

        var email = $"lockout_{Guid.NewGuid():N}@test.com";
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, "Password123!#", "Lock", "Out"))).EnsureSuccessStatusCode();

        // Identity's MaxFailedAccessAttempts is 5: after five failures the account is locked.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest(email, "WrongPassword123!"));
            failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var withCorrectPassword = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(email, "Password123!#"));

        withCorrectPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the lockout must hold even for the correct password");
        var body = await withCorrectPassword.Content.ReadAsStringAsync();
        body.Should().Contain("locked", "the response must distinguish lockout from bad credentials");
    }

    [Fact]
    public async Task RevokedRefreshToken_CannotRotate()
    {
        _factory.RequireContainers();

        var email = $"revoke_{Guid.NewGuid():N}@test.com";
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, "Password123!#", "Re", "Voke"))).EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!#"));
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();

        var authedClient = await _factory.CreateAuthenticatedClientAsync(email, "Password123!#");
        var revoke = await authedClient.PostAsJsonAsync("/api/v1/auth/revoke", new RevokeTokenRequest(auth!.RefreshToken));
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rotationAfterRevocation = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshTokenRequest(auth.RefreshToken));
        rotationAfterRevocation.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a revoked token must not be able to start a new session");
    }
}
