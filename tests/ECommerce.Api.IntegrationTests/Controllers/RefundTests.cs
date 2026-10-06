using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ECommerce.Api.IntegrationTests.Infrastructure;
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
/// Money taken for an order that is no longer fulfillable must flow back automatically: a payment
/// webhook landing on a cancelled order, and a Paid order cancelled by an admin, both end at
/// PaymentStatus.Refunded under the auto-refund policy (mock gateway accepts every refund).
/// </summary>
public class RefundTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RefundTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task PostCompletedWebhookAsync(HttpClient client, string sessionId, string paymentIntentId, string eventId)
    {
        var payload = new
        {
            id = eventId,
            type = "checkout.session.completed",
            data = new
            {
                @object = new { id = sessionId, payment_intent = paymentIntentId }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        client.DefaultRequestHeaders.Remove("Stripe-Signature");
        client.DefaultRequestHeaders.Add("Stripe-Signature", "test_signature");

        var response = await client.PostAsync("/api/v1/payments/webhook", content);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Webhook_ForCancelledOrder_RefundsThePayment()
    {
        _factory.RequireContainers();
        var client = _factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var sessionId = $"cs_refund_{Guid.NewGuid():N}";
        const string paymentIntentId = "pi_refund_test_1";

        Guid orderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.AsNoTracking().FirstAsync();
            var order = Order.Create(Guid.NewGuid(), new Address("Street 1", "City", "State", "12345", "Country"),
                now, now.AddMinutes(35), new[] { (product.Id, product.Name, product.Price, 1) });
            order.Cancel("Payment deadline expired", now);
            db.Orders.Add(order);
            db.Payments.Add(Payment.Create(order.Id, sessionId, "https://example.com", 50m, now.AddMinutes(31), now));
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        await PostCompletedWebhookAsync(client, sessionId, paymentIntentId, $"evt_refund_{Guid.NewGuid():N}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = await db.Payments.AsNoTracking().FirstAsync(p => p.OrderId == orderId);
            payment.Status.Should().Be(PaymentStatus.Refunded,
                "money taken for a cancelled order must be refunded automatically");
            payment.StripePaymentIntentId.Should().Be(paymentIntentId,
                "the late webhook's intent id must be recorded so the payment is refundable");
        }
    }

    [Fact]
    public async Task CancellingAPaidOrder_RefundsItsSucceededPayment()
    {
        _factory.RequireContainers();
        var admin = await _factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        const string paymentIntentId = "pi_refund_test_2";

        Guid orderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.AsNoTracking().FirstAsync();
            var order = Order.Create(Guid.NewGuid(), new Address("Street 2", "City", "State", "12345", "Country"),
                now, now.AddMinutes(35), new[] { (product.Id, product.Name, product.Price, 1) });
            db.Orders.Add(order);
            var payment = Payment.Create(order.Id, $"cs_paid_{Guid.NewGuid():N}", "https://example.com", 50m, now.AddMinutes(31), now);
            payment.MarkSucceeded(now, paymentIntentId);
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        var cancel = await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/cancel", new { Reason = "Customer changed their mind" });
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.Cancelled);

            var payment = await db.Payments.AsNoTracking().FirstAsync(p => p.OrderId == orderId);
            payment.Status.Should().Be(PaymentStatus.Refunded,
                "cancelling a Paid order must not leave customer money held");
        }
    }
}
