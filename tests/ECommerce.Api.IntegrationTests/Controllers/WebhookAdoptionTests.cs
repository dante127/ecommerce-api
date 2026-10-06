using System.Net;
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
/// Crash-consistency for checkout sessions: if the API created a Stripe session but died before
/// persisting the Payment row, the customer can still pay the orphaned session. The session
/// carries order_id metadata, so the completion webhook reconstructs (adopts) the missing payment
/// row and advances the order — the money state is never dropped. Retries of the session-creation
/// call additionally converge on the same Stripe session via the idempotency key.
/// </summary>
public class WebhookAdoptionTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public WebhookAdoptionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompletedWebhook_ForAnOrphanedSession_AdoptsThePaymentAndMarksTheOrderPaid()
    {
        _factory.RequireContainers();
        var client = _factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var sessionId = $"cs_orphan_{Guid.NewGuid():N}";
        var eventId = $"evt_orphan_{Guid.NewGuid():N}";

        // The order exists but no Payment row: the crashed-session-creation scenario.
        Guid orderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.AsNoTracking().FirstAsync();
            var order = Order.Create(Guid.NewGuid(), new Address("Street 3", "City", "State", "12345", "Country"),
                now, now.AddMinutes(35), new[] { (product.Id, product.Name, product.Price, 1) });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        var payload = new
        {
            id = eventId,
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    id = sessionId,
                    payment_intent = "pi_orphan_test",
                    metadata = new { order_id = orderId.ToString() }
                }
            }
        };

        client.DefaultRequestHeaders.Remove("Stripe-Signature");
        client.DefaultRequestHeaders.Add("Stripe-Signature", "test_signature");
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/payments/webhook", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.Paid, "the adopted payment must advance the order");

            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.StripeSessionId == sessionId);
            payment.Status.Should().Be(PaymentStatus.Succeeded);
            payment.StripePaymentIntentId.Should().Be("pi_orphan_test");
        }
    }

    [Fact]
    public async Task CompletedWebhook_ForAnOrphanedSessionOnACancelledOrder_AdoptsAndRefunds()
    {
        _factory.RequireContainers();
        var client = _factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var sessionId = $"cs_orphan_cxl_{Guid.NewGuid():N}";
        var eventId = $"evt_orphan_cxl_{Guid.NewGuid():N}";

        Guid orderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.AsNoTracking().FirstAsync();
            var order = Order.Create(Guid.NewGuid(), new Address("Street 4", "City", "State", "12345", "Country"),
                now, now.AddMinutes(35), new[] { (product.Id, product.Name, product.Price, 1) });
            order.Cancel("Payment deadline expired", now);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        var payload = new
        {
            id = eventId,
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    id = sessionId,
                    payment_intent = "pi_orphan_cxl_test",
                    metadata = new { order_id = orderId.ToString() }
                }
            }
        };

        client.DefaultRequestHeaders.Remove("Stripe-Signature");
        client.DefaultRequestHeaders.Add("Stripe-Signature", "test_signature");
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        (await client.PostAsync("/api/v1/payments/webhook", content)).EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.StripeSessionId == sessionId);
            payment.Status.Should().Be(PaymentStatus.Refunded,
                "adopted money for a cancelled order must follow the auto-refund policy");
        }
    }
}
