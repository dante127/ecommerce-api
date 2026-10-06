using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Cart.DTOs;
using ECommerce.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Application.Features.Cart.Commands;

// 1. Add Item to Cart
public sealed record AddItemToCartCommand(Guid ProductId, int Quantity) : IRequest<Result<CartResponse>>;

public sealed class AddItemToCartCommandValidator : AbstractValidator<AddItemToCartCommand>
{
    public AddItemToCartCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
    }
}

public sealed class AddItemToCartCommandHandler : IRequestHandler<AddItemToCartCommand, Result<CartResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public AddItemToCartCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<Result<CartResponse>> Handle(AddItemToCartCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || !_currentUserService.UserId.HasValue)
        {
            return Result<CartResponse>.Failure(Error.Unauthenticated);
        }

        var userId = _currentUserService.UserId.Value;

        // Verify product exists and is active
        var product = await _context.Products
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.IsActive, cancellationToken);

        if (product == null)
        {
            return Result<CartResponse>.Failure(Error.NotFound("Product.NotFound", "Product does not exist or is inactive."));
        }

        var availableStock = product.Inventory?.Quantity ?? 0;

        var now = _timeProvider.GetUtcNow();
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart == null)
        {
            cart = ECommerce.Domain.Entities.Cart.Create(userId, now);
            await _context.Carts.AddAsync(cart, cancellationToken);
        }

        // Validate the quantity the line WOULD hold, not just the increment: adding to an
        // existing line must not push it past available stock.
        var projectedQuantity = cart.ProjectedQuantity(request.ProductId, request.Quantity);
        if (projectedQuantity > availableStock)
        {
            return Result<CartResponse>.Failure(
                Error.Conflict("Cart.InsufficientStock", $"Requested total quantity ({projectedQuantity}) exceeds available stock ({availableStock})."));
        }

        cart.AddItem(request.ProductId, request.Quantity, now);
        await _context.SaveChangesAsync(cancellationToken);

        // Projected straight from the database: no tracked graph re-read, and an explicit
        // order so the response is deterministic.
        var items = await _context.LoadPurchasableLinesAsync(cart.Id, cancellationToken);

        return Result<CartResponse>.Success(new CartResponse(cart.Id, items, items.Sum(i => i.Subtotal)));
    }
}

// 2. Update Cart Item Quantity
public sealed record UpdateCartItemCommand(Guid ProductId, int Quantity) : IRequest<Result<CartResponse>>;

public sealed class UpdateCartItemCommandValidator : AbstractValidator<UpdateCartItemCommand>
{
    public UpdateCartItemCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0).WithMessage("Quantity cannot be negative.");
    }
}

public sealed class UpdateCartItemCommandHandler : IRequestHandler<UpdateCartItemCommand, Result<CartResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public UpdateCartItemCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<Result<CartResponse>> Handle(UpdateCartItemCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || !_currentUserService.UserId.HasValue)
        {
            return Result<CartResponse>.Failure(Error.Unauthenticated);
        }

        var userId = _currentUserService.UserId.Value;
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart == null)
        {
            return Result<CartResponse>.Failure(Error.NotFound("Cart.NotFound", "Cart not found."));
        }

        if (request.Quantity > 0)
        {
            var product = await _context.Products
                .Include(p => p.Inventory)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.IsActive, cancellationToken);

            if (product == null)
            {
                return Result<CartResponse>.Failure(Error.NotFound("Product.NotFound", "Product does not exist or is inactive."));
            }

            var availableStock = product.Inventory?.Quantity ?? 0;
            if (availableStock < request.Quantity)
            {
                return Result<CartResponse>.Failure(
                    Error.Conflict("Cart.InsufficientStock", $"Requested quantity ({request.Quantity}) exceeds available stock ({availableStock})."));
            }
        }

        var now = _timeProvider.GetUtcNow();
        cart.UpdateItemQuantity(request.ProductId, request.Quantity, now);
        await _context.SaveChangesAsync(cancellationToken);

        var items = await _context.LoadPurchasableLinesAsync(cart.Id, cancellationToken);

        return Result<CartResponse>.Success(new CartResponse(cart.Id, items, items.Sum(i => i.Subtotal)));
    }
}

// 3. Remove Item From Cart
public sealed record RemoveCartItemCommand(Guid ProductId) : IRequest<Result>;

public sealed class RemoveCartItemCommandHandler : IRequestHandler<RemoveCartItemCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public RemoveCartItemCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(RemoveCartItemCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || !_currentUserService.UserId.HasValue)
        {
            return Result.Failure(Error.Unauthenticated);
        }

        var userId = _currentUserService.UserId.Value;
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart != null)
        {
            var now = _timeProvider.GetUtcNow();
            cart.RemoveItem(request.ProductId, now);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

// 4. Clear Cart
public sealed record ClearCartCommand : IRequest<Result>;

public sealed class ClearCartCommandHandler : IRequestHandler<ClearCartCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public ClearCartCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(ClearCartCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || !_currentUserService.UserId.HasValue)
        {
            return Result.Failure(Error.Unauthenticated);
        }

        var userId = _currentUserService.UserId.Value;
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart != null)
        {
            var now = _timeProvider.GetUtcNow();
            cart.Clear(now);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
