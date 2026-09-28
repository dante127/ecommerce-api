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

public class WebhookIdempotencyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public WebhookIdempotencyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Webhook_DuplicateEvent_ProcessesIdempotentlyAndReturns200BothTimes()
    {
        _factory.RequireContainers();

        var sessionId = $"cs_test_webhook_{Guid.NewGuid():N}";
        var eventId = $"evt_test_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        // 1. Seed Order and Payment. The id comes from the domain factory: forcing it
        // through reflection does not work, because Id has a protected setter.
        Guid orderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var address = new Address("Street 1", "City", "State", "12345", "Country");
            var order = Order.Create(Guid.NewGuid(), address, now, now.AddMinutes(35), new[]
            {
                (Guid.NewGuid(), "Test Product", 50m, 1)
            });

            orderId = order.Id;

            var payment = Payment.Create(orderId, sessionId, "https://example.com", 50m, now.AddMinutes(31), now);

            db.Orders.Add(order);
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();

        var webhookPayload = new
        {
            id = eventId,
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    id = sessionId,
                    payment_intent = "pi_test_123456"
                }
            }
        };

        var json = JsonSerializer.Serialize(webhookPayload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // The header must be present; in mock Stripe mode its value is not verified.
        client.DefaultRequestHeaders.Remove("Stripe-Signature");
        client.DefaultRequestHeaders.Add("Stripe-Signature", "test_signature");

        // 2. Send Webhook First Time
        var firstResponse = await client.PostAsync("/api/v1/payments/webhook", content);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Order is Paid and Payment Succeeded
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.FirstAsync(o => o.Id == orderId);
            var payment = await db.Payments.FirstAsync(p => p.StripeSessionId == sessionId);
            var processedEvent = await db.ProcessedWebhookEvents.FirstOrDefaultAsync(e => e.StripeEventId == eventId);

            order.Status.Should().Be(OrderStatus.Paid);
            payment.Status.Should().Be(PaymentStatus.Succeeded);
            payment.StripePaymentIntentId.Should().Be("pi_test_123456");
            processedEvent.Should().NotBeNull();
        }

        // 3. Send Webhook Second Time (Identical payload - replay attack or Stripe retry)
        var secondContent = new StringContent(json, Encoding.UTF8, "application/json");
        var secondResponse = await client.PostAsync("/api/v1/payments/webhook", secondContent);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify that ProcessedWebhookEvents has exactly 1 entry for this eventId
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var eventCount = await db.ProcessedWebhookEvents.CountAsync(e => e.StripeEventId == eventId);
            eventCount.Should().Be(1, "webhook events must only be recorded and processed once");
        }
    }
}
