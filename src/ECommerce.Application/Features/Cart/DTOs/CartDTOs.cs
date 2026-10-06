namespace ECommerce.Application.Features.Cart.DTOs;

public sealed record CartItemResponse(
    Guid ProductId,
    string ProductName,
    string Sku,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal);

public sealed record CartResponse(
    Guid? CartId,
    IReadOnlyCollection<CartItemResponse> Items,
    decimal TotalAmount);

public sealed record AddItemToCartRequest(Guid ProductId, int Quantity);

public sealed record UpdateCartItemRequest(int Quantity);
