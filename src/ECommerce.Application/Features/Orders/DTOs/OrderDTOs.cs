using ECommerce.Domain.Enums;

namespace ECommerce.Application.Features.Orders.DTOs;

public sealed record AddressDto(
    string Street,
    string City,
    string State,
    string PostalCode,
    string Country);

public sealed record CheckoutRequest(AddressDto ShippingAddress);

public sealed record CancelOrderRequest(string? Reason);

public sealed record TransitionOrderStatusRequest(OrderStatus Status);

public sealed record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal);

public sealed record PaymentSummaryResponse(
    Guid Id,
    string Status,
    decimal Amount,
    string? StripePaymentIntentId,
    DateTimeOffset CreatedAt);

public sealed record OrderSummaryResponse(
    Guid Id,
    string Status,
    decimal TotalAmount,
    int ItemCount,
    DateTimeOffset PaymentDeadline,
    DateTimeOffset CreatedAt);

public sealed record OrderResponse(
    Guid Id,
    Guid UserId,
    string Status,
    decimal TotalAmount,
    AddressDto ShippingAddress,
    DateTimeOffset PaymentDeadline,
    string? CancellationReason,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<OrderItemResponse> Items,
    IReadOnlyCollection<PaymentSummaryResponse> Payments);
