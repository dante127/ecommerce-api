using System.Net.Http.Headers;
using System.Net.Http.Json;
using ECommerce.Application.Features.Auth.DTOs;
using ECommerce.Infrastructure.Identity;
using ECommerce.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _dbContainer;
    private RedisContainer? _redisContainer;
    public bool IsContainerReady { get; private set; }
    public string? ContainerFailureReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _dbContainer = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("ecommerce_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            _redisContainer = new RedisBuilder("redis:7-alpine")
                .Build();

            await _dbContainer.StartAsync();
            await _redisContainer.StartAsync();

            IsContainerReady = true;

            await EnsureDatabaseSeededAsync();
        }
        catch (Exception ex)
        {
            IsContainerReady = false;
            ContainerFailureReason = $"{ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"[Testcontainers] Docker not available: {ex.Message}");
        }
    }

    /// <summary>
    /// Fails the calling test when the container dependency is missing. These tests
    /// exercise real HTTP against PostgreSQL and Redis, so a run without them proves
    /// nothing and must never be reported as passing.
    /// </summary>
    public void RequireContainers()
    {
        if (IsContainerReady)
        {
            return;
        }

        throw new InvalidOperationException(
            "This integration test requires Docker (Testcontainers PostgreSQL 17 + Redis 7). " +
            "Start Docker and re-run, or run the unit test projects only. " +
            $"Container startup failure: {ContainerFailureReason}");
    }

    private async Task EnsureDatabaseSeededAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CustomWebApplicationFactory>>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        await context.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(context, userManager, roleManager, logger, timeProvider);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_dbContainer != null && _redisContainer != null)
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _dbContainer.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Redis", _redisContainer.GetConnectionString());
            // The concurrency tests register ten users from a single address, so the auth
            // rate limit has to be raised for the test host.
            builder.UseSetting("RateLimiting:Auth:PermitLimit", "10000");
            builder.UseSetting("Stripe:SecretKey", "sk_test_placeholder");
            builder.UseSetting("Stripe:WebhookSecret", "whsec_placeholder");
        }
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        return client;
    }

    public new async Task DisposeAsync()
    {
        if (_dbContainer != null)
        {
            await _dbContainer.DisposeAsync();
        }

        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
