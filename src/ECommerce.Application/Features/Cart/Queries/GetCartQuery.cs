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

    public GetCartQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<CartResponse>> Handle(GetCartQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || !_currentUserService.UserId.HasValue)
        {
            return Result<CartResponse>.Failure(Error.Unauthorized("Auth.Unauthorized", "User is not authenticated."));
        }

        var userId = _currentUserService.UserId.Value;

        var cart = await _context.Carts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart == null)
        {
            // A GET must not mutate state, so no cart row is created here. The cart is
            // created by the first cart write; an empty projection is returned instead.
            return Result<CartResponse>.Success(
                new CartResponse(Guid.Empty, Array.Empty<CartItemResponse>(), 0m));
        }

        // Projected straight from the database rather than from the tracked cart graph.
        var items = await _context.LoadActiveLinesAsync(cart.Id, cancellationToken);

        var totalAmount = items.Sum(i => i.Subtotal);
        var response = new CartResponse(cart.Id, items, totalAmount);

        return Result<CartResponse>.Success(response);
    }
}
