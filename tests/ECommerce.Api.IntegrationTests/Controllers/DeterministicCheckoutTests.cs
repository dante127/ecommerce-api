using System.Net;
using System.Net.Http.Json;
using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Application.Features.Auth.DTOs;
using ECommerce.Application.Features.Cart.DTOs;
using ECommerce.Application.Features.Orders.DTOs;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

public class DeterministicCheckoutTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DeterministicCheckoutTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrencyCheckout_WhenTenUsersCheckoutProductWithStockTen_AllTenSucceedAndStockReachesZero()
    {
        _factory.RequireContainers();

        var client = _factory.CreateClient();

        // 1. Prepare 10 distinct registered and authenticated users
        const int userCount = 10;
        var clients = new List<HttpClient>();

        for (int i = 0; i < userCount; i++)
        {
            var email = $"exact_stock_user_{i}_{Guid.NewGuid():N}@test.com";
            var password = "Password123!#";

            var regResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(
                email, password, $"User{i}", "ExactStock"));
            regResponse.EnsureSuccessStatusCode();

            var userClient = await _factory.CreateAuthenticatedClientAsync(email, password);
            clients.Add(userClient);
        }

        // 2. Create or reset a product with initial stock = 10
        Guid productId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = await db.Categories.FirstAsync();
            var product = Product.Create(
                $"TECH-DET-{Guid.NewGuid():N}".Substring(0, 20),
                "Deterministic Test Item",
                "Product for deterministic concurrency test",
                100.00m,
                category.Id,
                DateTimeOffset.UtcNow);

            var inventory = InventoryItem.Create(product.Id, 10, DateTimeOffset.UtcNow);

            db.Products.Add(product);
            db.InventoryItems.Add(inventory);
            await db.SaveChangesAsync();

            productId = product.Id;
        }

        // 3. Each user adds 1 unit to their cart
        foreach (var userClient in clients)
        {
            var addResponse = await userClient.PostAsJsonAsync("/api/v1/cart/items", new AddItemToCartRequest(productId, 1));
            addResponse.EnsureSuccessStatusCode();
        }

        // 4. Trigger 10 parallel checkouts concurrently
        var shippingAddress = new AddressDto("100 University Ave", "Amman", "Amman", "11118", "Jordan");
        var checkoutRequest = new CheckoutRequest(shippingAddress);

        var checkoutTasks = clients.Select(c => c.PostAsJsonAsync("/api/v1/orders", checkoutRequest)).ToList();
        var responses = await Task.WhenAll(checkoutTasks);

        // 5. Assert: All 10 requests get 201 Created
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        successCount.Should().Be(10, "all ten users must succeed because stock is exactly 10");

        // 6. Assert DB final stock is exactly 0
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var finalStock = await db.InventoryItems
                .Where(x => x.ProductId == productId)
                .Select(x => x.Quantity)
                .FirstAsync();

            finalStock.Should().Be(0, "final inventory must reach exactly 0");
        }
    }
}
