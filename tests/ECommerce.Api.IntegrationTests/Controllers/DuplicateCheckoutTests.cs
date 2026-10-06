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

/// <summary>
/// Two concurrent checkouts by the SAME user used to both observe the populated cart and create
/// duplicate orders (each reserving stock). The per-user checkout serialization makes the loser
/// wait on the advisory lock, observe the emptied cart and fail with EmptyCart — the same outcome
/// as a second click after the first checkout completes.
/// </summary>
public class DuplicateCheckoutTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DuplicateCheckoutTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TwoSimultaneousCheckoutsByOneUser_CreateExactlyOneOrder()
    {
        _factory.RequireContainers();

        var client = _factory.CreateClient();
        var email = $"dup_checkout_{Guid.NewGuid():N}@test.com";
        const string password = "Password123!#";
        const int initialStock = 10;
        const int quantity = 2;

        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Dup", "Checkout"));
        register.EnsureSuccessStatusCode();
        var userClient = await _factory.CreateAuthenticatedClientAsync(email, password);

        Guid productId;
        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = await db.Categories.FirstAsync();
            var product = Product.Create(
                $"TECH-DUP-{Guid.NewGuid():N}".Substring(0, 20),
                "Duplicate Checkout Item",
                "Product for the duplicate checkout serialization test",
                25m,
                category.Id,
                DateTimeOffset.UtcNow);
            db.Products.Add(product);
            db.InventoryItems.Add(InventoryItem.Create(product.Id, initialStock, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            productId = product.Id;

            userId = await db.Users
                .Where(u => u.Email == email)
                .Select(u => u.Id)
                .FirstAsync();
        }

        (await userClient.PostAsJsonAsync("/api/v1/cart/items", new AddItemToCartRequest(productId, quantity)))
            .EnsureSuccessStatusCode();

        var shippingAddress = new AddressDto("5 Dup St", "Amman", "Amman", "11118", "Jordan");
        var first = userClient.PostAsJsonAsync("/api/v1/orders", new CheckoutRequest(shippingAddress));
        var second = userClient.PostAsJsonAsync("/api/v1/orders", new CheckoutRequest(shippingAddress));
        var responses = await Task.WhenAll(first, second);

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1,
            "exactly one of the two simultaneous checkouts may win");
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(1,
            "the loser must observe the emptied cart, not create a second order");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var orderCount = await db.Orders.CountAsync(o => o.UserId == userId);
            orderCount.Should().Be(1, "duplicate orders must not be created");

            var stock = await db.InventoryItems
                .Where(i => i.ProductId == productId)
                .Select(i => i.Quantity)
                .FirstAsync();
            stock.Should().Be(initialStock - quantity, "only the winning order reserved stock");
        }
    }
}
