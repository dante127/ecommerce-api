using System.Linq.Expressions;
using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Features.Cart.DTOs;
using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Application.Features.Cart;

/// <summary>
/// Read side of the cart: projects cart lines for API responses in one ordered query, instead of
/// materialising the tracked Cart/Items/Product graph and mapping it in memory.
/// </summary>
internal static class CartLineQueries
{
    private static readonly Expression<Func<CartItem, bool>> Purchasable =
        i => i.Product != null && !i.Product.IsDeleted;

    private static readonly Expression<Func<CartItem, bool>> PurchasableAndActive =
        i => i.Product != null && !i.Product.IsDeleted && i.Product.IsActive;

    private static readonly Expression<Func<CartItem, CartItemResponse>> ToResponse =
        i => new CartItemResponse(
            i.ProductId,
            i.Product!.Name,
            i.Product.Sku,
            i.Product.Price,
            i.Quantity,
            i.Product.Price * i.Quantity);

    /// <summary>Lines the caller may still buy or edit: product exists and is not soft-deleted.</summary>
    internal static Task<List<CartItemResponse>> LoadPurchasableLinesAsync(
        this IApplicationDbContext context,
        Guid cartId,
        CancellationToken cancellationToken)
        => context.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cartId)
            .Where(Purchasable)
            .OrderBy(i => i.Product!.Name)
            .Select(ToResponse)
            .ToListAsync(cancellationToken);

    /// <summary>Lines shown on the cart screen: also requires the product to be active.</summary>
    internal static Task<List<CartItemResponse>> LoadActiveLinesAsync(
        this IApplicationDbContext context,
        Guid cartId,
        CancellationToken cancellationToken)
        => context.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cartId)
            .Where(PurchasableAndActive)
            .OrderBy(i => i.Product!.Name)
            .Select(ToResponse)
            .ToListAsync(cancellationToken);
}
