using ECommerce.Application.Features.Orders.DTOs;
using ECommerce.Domain.Entities;

namespace ECommerce.Application.Features.Orders;

/// <summary>Single mapping from the Order aggregate to its API representation.</summary>
internal static class OrderResponseMapper
{
    internal static OrderResponse ToResponse(this Order order, IReadOnlyCollection<PaymentSummaryResponse> payments)
        => new(
            order.Id,
            order.UserId,
            order.Status.ToString(),
            order.TotalAmount,
            new AddressDto(
                order.ShippingAddress.Street,
                order.ShippingAddress.City,
                order.ShippingAddress.State,
                order.ShippingAddress.PostalCode,
                order.ShippingAddress.Country),
            order.PaymentDeadline,
            order.CancellationReason,
            order.RowVersion,
            order.CreatedAt,
            order.Items
                .Select(item => new OrderItemResponse(
                    item.ProductId,
                    item.ProductName,
                    item.UnitPrice,
                    item.Quantity,
                    item.UnitPrice * item.Quantity))
                .ToList(),
            payments);
}
