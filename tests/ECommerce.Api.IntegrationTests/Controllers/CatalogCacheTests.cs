using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Products.DTOs;
using ECommerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

/// <summary>
/// The catalog cache had no coverage at all, so a payload that failed to deserialize would have
/// degraded every read to a silent cache miss with nothing failing anywhere.
/// </summary>
public class CatalogCacheTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string ListingUrl = "/api/v1/products?page=1&pageSize=20";

    /// <summary>
    /// The API serializes enums as strings (JsonStringEnumConverter) while GetFromJsonAsync defaults
    /// to numeric enums, so responses must be read back with the API own options.
    /// </summary>
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CustomWebApplicationFactory _factory;

    public CatalogCacheTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProductListing_IsCached_AndTheNextRequestIsServedFromThatEntry()
    {
        _factory.RequireContainers();

        var client = _factory.CreateClient();

        // 1. Warm the cache with the default listing.
        var warm = await client.GetFromJsonAsync<PagedList<ProductResponse>>(ListingUrl, ApiJson);
        warm.Should().NotBeNull();
        warm!.Items.Should().NotBeEmpty();

        // 2. Change the underlying row without touching the cache version.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.OrderBy(p => p.Name).FirstAsync();
            product.UpdateDetails(
                product.Name + " (changed)",
                product.Description,
                product.CategoryId,
                DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        // 3. The identical request must still return the cached view. If the cached payload failed to
        //    deserialize, the cache read returns null, the query falls through to PostgreSQL and this
        //    assertion fails - which is exactly the silent degradation this test exists to catch.
        var second = await client.GetFromJsonAsync<PagedList<ProductResponse>>(ListingUrl, ApiJson);
        second.Should().NotBeNull();
        second!.Items.Should().BeEquivalentTo(warm.Items);

        // 4. And an entry must exist under the current catalog version.
        using (var scope = _factory.Services.CreateScope())
        {
            var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();
            var version = await cacheService.GetVersionAsync("catalog:version");
            var pattern = $"ECommerce_catalog:v{version}:products:*";

            var found = false;
            foreach (var endpoint in redis.GetEndPoints())
            {
                var server = redis.GetServer(endpoint);
                if (!server.IsConnected)
                {
                    continue;
                }

                await foreach (var _ in server.KeysAsync(pattern: pattern))
                {
                    found = true;
                    break;
                }
            }

            found.Should().BeTrue($"the default listing is a cacheable shape and must be stored under {pattern}");
        }
    }
}
