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

public class ConcurrencyCheckoutTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ConcurrencyCheckoutTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrencyCheckout_WhenTenUsersCheckoutProductWithStockOne_ExactlyOneSucceedsAndNineFailWith409()
    {
        _factory.RequireContainers();

        // 1. Find product with stock = 1 (TECH-GPU-001)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var gpu = await db.Products.FirstOrDefaultAsync(p => p.Sku == "TECH-GPU-001");
            gpu.Should().NotBeNull();
        }

        var client = _factory.CreateClient();

        // 2. Prepare 10 distinct registered and authenticated users
        const int userCount = 10;
        var clients = new List<HttpClient>();

        for (int i = 0; i < userCount; i++)
        {
            var email = $"contention_user_{i}_{Guid.NewGuid():N}@test.com";
            var password = "Password123!#";

            var regResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(
                email, password, $"User{i}", "Test"));
            regResponse.EnsureSuccessStatusCode();

            var userClient = await _factory.CreateAuthenticatedClientAsync(email, password);
            clients.Add(userClient);
        }

        // Find GPU ID from database
        Guid gpuId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var gpu = await db.Products.Include(p => p.Inventory).FirstAsync(p => p.Sku == "TECH-GPU-001");
            gpuId = gpu.Id;

            // Reset stock to 1 just in case
            await db.InventoryItems
                .Where(x => x.ProductId == gpuId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, 1));
        }

        // 3. Each user adds the single GPU to their cart
        foreach (var userClient in clients)
        {
            var addResponse = await userClient.PostAsJsonAsync("/api/v1/cart/items", new AddItemToCartRequest(gpuId, 1));
            addResponse.EnsureSuccessStatusCode();
        }

        // 4. Trigger 10 parallel checkouts concurrently
        var shippingAddress = new AddressDto("123 King St", "Amman", "Amman", "11118", "Jordan");
        var checkoutRequest = new CheckoutRequest(shippingAddress);

        var checkoutTasks = clients.Select(c => c.PostAsJsonAsync("/api/v1/orders", checkoutRequest)).ToList();
        var responses = await Task.WhenAll(checkoutTasks);

        // 5. Assert: Exactly 1 request gets 201 Created and 9 get 409 Conflict
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, "exactly one user must successfully reserve the single available item");
        conflictCount.Should().Be(9, "all other nine users must receive a 409 Conflict");

        // 6. Assert DB final stock is exactly 0, never negative
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var finalStock = await db.InventoryItems
                .Where(x => x.ProductId == gpuId)
                .Select(x => x.Quantity)
                .FirstAsync();

            finalStock.Should().Be(0, "inventory must be exactly 0 and never drop below zero");
        }
    }
}
