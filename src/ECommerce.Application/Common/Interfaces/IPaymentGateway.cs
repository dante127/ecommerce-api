namespace ECommerce.Application.Common.Interfaces;

public sealed record PaymentSessionResult(string SessionId, string CheckoutUrl);

public sealed record WebhookEventResult(
    string EventId,
    string EventType,
    string? SessionId,
    string? PaymentIntentId,
    string? OrderIdFromMetadata);

public interface IPaymentGateway
{
    Task<PaymentSessionResult> CreateCheckoutSessionAsync(
        Guid orderId,
        decimal amount,
        string currency,
        IReadOnlyCollection<(string Name, decimal UnitPrice, int Quantity)> items,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    WebhookEventResult VerifyAndParseWebhook(string payload, string signatureHeader);

    /// <summary>
    /// Refunds the full amount of a payment intent. Returns false when the gateway rejects the
    /// refund, so the payment can stay flagged RequiresRefund for operational follow-up.
    /// </summary>
    Task<bool> TryRefundAsync(string paymentIntentId, CancellationToken cancellationToken = default);
}
