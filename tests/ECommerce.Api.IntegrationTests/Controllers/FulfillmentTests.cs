using System.Net;
using System.Net.Http.Json;
using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Application.Features.Auth.DTOs;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.ValueObjects;
using ECommerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

/// <summary>
/// The fulfillment state machine (Paid → Processing → Shipped → Delivered) existed in the domain
/// but was unreachable. These tests pin the admin-only transitions, the state-machine rejections,
/// and the forbidden path for non-admins.
/// </summary>
public class FulfillmentTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public FulfillmentTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid OrderId, string CustomerEmail)> SeedPaidOrderAsync()
    {
        var email = $"fulfil_customer_{Guid.NewGuid():N}@test.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.AsNoTracking().FirstAsync();
            var order = Order.Create(Guid.NewGuid(), new Address("Street 9", "City", "State", "12345", "Country"),
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(35),
                new[] { (product.Id, product.Name, product.Price, 1) });
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            var tracked = await db.Orders.FirstAsync(o => o.Id == order.Id);
            tracked.MarkAsPaid(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            return (order.Id, email);
        }
    }

    [Fact]
    public async Task Admin_CanAdvanceFulfillment_ThroughTheWholeLifecycle()
    {
        _factory.RequireContainers();
        var admin = await _factory.CreateAdminClientAsync();
        var (orderId, _) = await SeedPaidOrderAsync();

        var shipTooEarly = await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/status",
            new { Status = OrderStatus.Shipped.ToString() });
        shipTooEarly.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a Paid order cannot ship before processing");

        foreach (var (target, expected) in new[]
                 {
                     (OrderStatus.Processing, HttpStatusCode.OK),
                     (OrderStatus.Shipped, HttpStatusCode.OK),
                     (OrderStatus.Delivered, HttpStatusCode.OK)
                 })
        {
            var response = await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/status",
                new { Status = target.ToString() });
            response.StatusCode.Should().Be(expected, $"transition to {target} must succeed in order");
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var finalStatus = await db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => o.Status)
            .FirstAsync();
        finalStatus.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public async Task Customer_CannotAdvanceFulfillment()
    {
        _factory.RequireContainers();
        var admin = await _factory.CreateAdminClientAsync();
        var (orderId, _) = await SeedPaidOrderAsync();

        var customer = _factory.CreateClient();
        var email = $"fulfil_other_{Guid.NewGuid():N}@test.com";
        (await customer.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, "Password123!#", "Other", "User"))).EnsureSuccessStatusCode();
        var customerClient = await _factory.CreateAuthenticatedClientAsync(email, "Password123!#");

        var response = await customerClient.PostAsJsonAsync($"/api/v1/orders/{orderId}/status",
            new { Status = OrderStatus.Processing.ToString() });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
