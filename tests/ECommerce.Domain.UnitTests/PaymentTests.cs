using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace ECommerce.Domain.UnitTests;

public class PaymentTests
{
    [Fact]
    public void Create_ValidParameters_CreatesPendingPayment()
    {
        var orderId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(31);

        var payment = Payment.Create(
            orderId,
            "cs_test_123",
            "https://checkout.stripe.com/pay/cs_test_123",
            199.99m,
            expiresAt,
            now);

        payment.OrderId.Should().Be(orderId);
        payment.StripeSessionId.Should().Be("cs_test_123");
        payment.CheckoutUrl.Should().Be("https://checkout.stripe.com/pay/cs_test_123");
        payment.Amount.Should().Be(199.99m);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.ExpiresAt.Should().Be(expiresAt);
        payment.CreatedAt.Should().Be(now);
    }

    [Theory]
    [InlineData("", "url", 10)]
    [InlineData("cs_123", "", 10)]
    [InlineData("cs_123", "url", 0)]
    [InlineData("cs_123", "url", -5)]
    public void Create_InvalidParameters_ThrowsDomainException(string sessionId, string url, decimal amount)
    {
        var now = DateTimeOffset.UtcNow;
        var act = () => Payment.Create(Guid.NewGuid(), sessionId, url, amount, now.AddMinutes(30), now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkSucceeded_UpdatesStatusAndPaymentIntentId()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = Payment.Create(Guid.NewGuid(), "cs_123", "https://checkout.stripe.com", 50m, now.AddMinutes(30), now);

        payment.MarkSucceeded(now.AddMinutes(5), "pi_98765");

        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.StripePaymentIntentId.Should().Be("pi_98765");
        payment.UpdatedAt.Should().Be(now.AddMinutes(5));
    }

    [Fact]
    public void MarkRequiresRefund_UpdatesStatusToRequiresRefund()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = Payment.Create(Guid.NewGuid(), "cs_123", "https://checkout.stripe.com", 50m, now.AddMinutes(30), now);

        payment.MarkRequiresRefund(now.AddMinutes(10));

        payment.Status.Should().Be(PaymentStatus.RequiresRefund);
        payment.UpdatedAt.Should().Be(now.AddMinutes(10));
    }

    [Fact]
    public void MarkExpired_WhenPending_UpdatesStatusToExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = Payment.Create(Guid.NewGuid(), "cs_123", "https://checkout.stripe.com", 50m, now.AddMinutes(30), now);

        payment.MarkExpired(now.AddMinutes(35));

        payment.Status.Should().Be(PaymentStatus.Expired);
    }

    [Fact]
    public void ProcessedWebhookEvent_Create_WithValidData_Succeeds()
    {
        var now = DateTimeOffset.UtcNow;
        var evt = ProcessedWebhookEvent.Create("evt_123", "checkout.session.completed", now);

        evt.StripeEventId.Should().Be("evt_123");
        evt.EventType.Should().Be("checkout.session.completed");
        evt.ProcessedAt.Should().Be(now);
    }

    [Fact]
    public void MarkRefunded_FromRequiresRefund_TransitionsToRefunded()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = Payment.Create(Guid.NewGuid(), "cs_x", "https://example.com", 10m, now.AddMinutes(30), now);
        payment.MarkRequiresRefund(now);

        payment.MarkRefunded(now);

        payment.Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public void MarkRefunded_FromPending_ThrowsDomainException()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = Payment.Create(Guid.NewGuid(), "cs_x", "https://example.com", 10m, now.AddMinutes(30), now);

        var act = () => payment.MarkRefunded(now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkRequiresRefund_RecordsThePaymentIntentId()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = Payment.Create(Guid.NewGuid(), "cs_x", "https://example.com", 10m, now.AddMinutes(30), now);

        payment.MarkRequiresRefund(now, "pi_late_webhook");

        payment.Status.Should().Be(PaymentStatus.RequiresRefund);
        payment.StripePaymentIntentId.Should().Be("pi_late_webhook");
    }
}
