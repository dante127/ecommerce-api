namespace ECommerce.Application.Features.Payments.DTOs;

public sealed record CheckoutSessionResponse(
    Guid PaymentId,
    string StripeSessionId,
    string CheckoutUrl,
    DateTimeOffset ExpiresAt);

public sealed record PaymentDetailsResponse(
    Guid Id,
    Guid OrderId,
    string Status,
    decimal Amount,
    string CheckoutUrl,
    DateTimeOffset ExpiresAt,
    string? StripePaymentIntentId,
    DateTimeOffset CreatedAt);
