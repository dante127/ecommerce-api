using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Cart.DTOs;
using ECommerce.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Application.Features.Cart.Queries;

public sealed record GetCartQuery : IRequest<Result<CartResponse>>;

public sealed class GetCartQueryHandler : IRequestHandler<GetCartQuery, Result<CartResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public GetCartQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<Result<CartResponse>> Handle(GetCartQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || !_currentUserService.UserId.HasValue)
        {
            return Result<CartResponse>.Failure(Error.Unauthorized("Auth.Unauthorized", "User is not authenticated."));
        }

        var userId = _currentUserService.UserId.Value;

        var cart = await _context.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart == null)
        {
            var now = _timeProvider.GetUtcNow();
            cart = ECommerce.Domain.Entities.Cart.Create(userId, now);
            await _context.Carts.AddAsync(cart, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var items = cart.Items
            .Where(i => i.Product != null && !i.Product.IsDeleted && i.Product.IsActive)
            .Select(i => new CartItemResponse(
                i.ProductId,
                i.Product!.Name,
                i.Product.Sku,
                i.Product.Price,
                i.Quantity,
                i.Product.Price * i.Quantity))
            .ToList();

        var totalAmount = items.Sum(i => i.Subtotal);
        var response = new CartResponse(cart.Id, items, totalAmount);

        return Result<CartResponse>.Success(response);
    }
}
