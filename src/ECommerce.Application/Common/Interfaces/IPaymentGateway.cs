namespace ECommerce.Application.Common.Interfaces;

public sealed record PaymentSessionResult(string SessionId, string CheckoutUrl);

public sealed record WebhookEventResult(
    string EventId,
    string EventType,
    string? SessionId,
    string? PaymentIntentId);

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
}
