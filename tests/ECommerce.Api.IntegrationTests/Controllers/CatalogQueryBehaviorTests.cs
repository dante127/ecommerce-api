using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Products.DTOs;
using ECommerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

/// <summary>
/// The API serializes enums as strings (JsonStringEnumConverter) while GetFromJsonAsync defaults
/// to numeric enums, so responses must be read back with the API's own options.
/// </summary>
internal static class ApiJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>
/// Client paging is normalized in one place: out-of-range pages fall to 1 and oversize page sizes
/// to the default, no matter what the caller sends.
/// </summary>
public class PaginationBoundaryTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PaginationBoundaryTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(0, 0, 1, 20)]
    [InlineData(-3, 500, 1, 20)]
    [InlineData(2, 100, 2, 100)]
    [InlineData(1, 101, 1, 20)]
    public async Task CatalogListing_NormalizesClientSuppliedPaging(
        int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        _factory.RequireContainers();
        var client = _factory.CreateClient();

        var response = await client.GetFromJsonAsync<PagedList<ProductResponse>>(
            $"/api/v1/products?page={page}&pageSize={pageSize}", ApiJson.Options);

        response.Should().NotBeNull();
        response!.PageNumber.Should().Be(expectedPage);
        response.PageSize.Should().Be(expectedPageSize);
    }
}

/// <summary>
/// A catalogue mutation must be visible on the very next listing request: the version counter
/// invalidation (ADR-006) may never serve a pre-mutation cached page after a command succeeded.
/// </summary>
public class CatalogInvalidationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string ListingUrl = "/api/v1/products?page=1&pageSize=20";

    private readonly CustomWebApplicationFactory _factory;

    public CatalogInvalidationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreatingAProduct_IsVisibleOnTheNextListingRequest()
    {
        _factory.RequireContainers();
        var admin = await _factory.CreateAdminClientAsync();

        var warm = await admin.GetFromJsonAsync<PagedList<ProductResponse>>(ListingUrl, ApiJson.Options);
        warm.Should().NotBeNull();

        // Product.Create uppercases SKUs, so the comparison value must be uppercase too.
        var sku = $"TECH-INV-{Guid.NewGuid():N}".Substring(0, 20).ToUpperInvariant();
        Guid categoryId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            categoryId = await db.Categories.Select(c => c.Id).FirstAsync();
        }

        var create = await admin.PostAsJsonAsync("/api/v1/products", new
        {
            sku,
            name = "Cache Invalidation Probe",
            description = "Product created to verify catalogue cache invalidation",
            price = 9.99m,
            initialStock = 5,
            categoryId
        });
        create.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        var after = await admin.GetFromJsonAsync<PagedList<ProductResponse>>(ListingUrl, ApiJson.Options);
        after.Should().NotBeNull();
        after!.Items.Should().Contain(i => i.Sku == sku,
            "the version-bumped cache must serve the post-mutation catalogue");
    }
}
