using ECommerce.Domain.Common;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Exceptions;

namespace ECommerce.Domain.Entities;

public sealed class Payment : AggregateRoot<Guid>
{
    public Guid OrderId { get; private set; }
    public string StripeSessionId { get; private set; } = null!;
    public string CheckoutUrl { get; private set; } = null!;
    public PaymentStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? StripePaymentIntentId { get; private set; }

    public Order Order { get; private set; } = null!;

    private Payment() { }

    public static Payment Create(Guid orderId, string stripeSessionId, string checkoutUrl, decimal amount, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (orderId == Guid.Empty)
            throw new DomainException("OrderId is required.");

        if (string.IsNullOrWhiteSpace(stripeSessionId))
            throw new DomainException("StripeSessionId is required.");

        if (string.IsNullOrWhiteSpace(checkoutUrl))
            throw new DomainException("CheckoutUrl is required.");

        if (amount <= 0)
            throw new DomainException("Payment amount must be greater than zero.");

        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            StripeSessionId = stripeSessionId.Trim(),
            CheckoutUrl = checkoutUrl.Trim(),
            Amount = amount,
            Status = PaymentStatus.Pending,
            ExpiresAt = expiresAt,
            CreatedAt = now
        };
    }

    public void MarkSucceeded(DateTimeOffset now, string? stripePaymentIntentId = null)
    {
        Status = PaymentStatus.Succeeded;
        StripePaymentIntentId = stripePaymentIntentId;
        UpdatedAt = now;
    }

    public void MarkExpired(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Pending)
        {
            Status = PaymentStatus.Expired;
            UpdatedAt = now;
        }
    }

    public void MarkRequiresRefund(DateTimeOffset now, string? stripePaymentIntentId = null)
    {
        Status = PaymentStatus.RequiresRefund;

        // A late completed webhook carries the intent id; recording it here is what makes the
        // flagged payment refundable even though MarkSucceeded never ran for it.
        if (!string.IsNullOrWhiteSpace(stripePaymentIntentId))
        {
            StripePaymentIntentId = stripePaymentIntentId;
        }

        UpdatedAt = now;
    }

    public void MarkRefunded(DateTimeOffset now)
    {
        if (Status != PaymentStatus.RequiresRefund && Status != PaymentStatus.Succeeded)
            throw new DomainException($"Cannot refund a payment in status '{Status}'.");

        Status = PaymentStatus.Refunded;
        UpdatedAt = now;
    }
}
