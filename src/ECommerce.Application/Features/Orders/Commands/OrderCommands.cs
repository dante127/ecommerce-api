using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Orders.DTOs;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Application.Features.Orders.Commands;

// 1. Checkout Command
public sealed record CheckoutCommand(AddressDto ShippingAddress) : IRequest<Result<OrderResponse>>;

public sealed class CheckoutCommandValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutCommandValidator()
    {
        RuleFor(x => x.ShippingAddress)
            .NotNull().WithMessage("Shipping address is required.");

        When(x => x.ShippingAddress != null, () =>
        {
            RuleFor(x => x.ShippingAddress.Street)
                .NotEmpty().WithMessage("Street is required.")
                .MaximumLength(200).WithMessage("Street cannot exceed 200 characters.");

            RuleFor(x => x.ShippingAddress.City)
                .NotEmpty().WithMessage("City is required.")
                .MaximumLength(100).WithMessage("City cannot exceed 100 characters.");

            RuleFor(x => x.ShippingAddress.State)
                .NotEmpty().WithMessage("State is required.")
                .MaximumLength(100).WithMessage("State cannot exceed 100 characters.");

            RuleFor(x => x.ShippingAddress.PostalCode)
                .NotEmpty().WithMessage("Postal code is required.")
                .MaximumLength(20).WithMessage("Postal code cannot exceed 20 characters.");

            RuleFor(x => x.ShippingAddress.Country)
                .NotEmpty().WithMessage("Country is required.")
                .MaximumLength(100).WithMessage("Country cannot exceed 100 characters.");
        });
    }
}

public sealed class CheckoutCommandHandler : IRequestHandler<CheckoutCommand, Result<OrderResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICacheService _cacheService;
    private readonly TimeProvider _timeProvider;

    public CheckoutCommandHandler(
        IApplicationDbContext context,
        IInventoryService inventoryService,
        ICurrentUserService currentUserService,
        ICacheService cacheService,
        TimeProvider timeProvider)
    {
        _context = context;
        _inventoryService = inventoryService;
        _currentUserService = currentUserService;
        _cacheService = cacheService;
        _timeProvider = timeProvider;
    }

    public async Task<Result<OrderResponse>> Handle(CheckoutCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return Result<OrderResponse>.Failure(Error.Unauthorized("Auth.Unauthorized", "User is not authenticated."));
        }

        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(c => c.UserId == userId.Value, cancellationToken);

        if (cart == null || !cart.Items.Any())
        {
            return Result<OrderResponse>.Failure(
                Error.BadRequest("Checkout.EmptyCart", "Cannot checkout with an empty cart."));
        }

        // Validate that all products still exist and can be purchased
        foreach (var item in cart.Items)
        {
            if (item.Product == null || item.Product.IsDeleted || !item.Product.IsActive)
            {
                return Result<OrderResponse>.Failure(
                    Error.BadRequest("Checkout.ProductUnavailable", $"Product '{item.ProductId}' is no longer available."));
            }
        }

        // Deadlock prevention: sort items deterministically by ProductId before reserving
        var sortedItems = cart.Items.OrderBy(i => i.ProductId).ToList();

        var now = _timeProvider.GetUtcNow();
        var paymentDeadline = now.AddMinutes(35);

        // Begin database transaction for atomic checkout and inventory reservation
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in sortedItems)
            {
                var reserved = await _inventoryService.TryReserveAsync(item.ProductId, item.Quantity, cancellationToken);
                if (!reserved)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<OrderResponse>.Failure(
                        Error.Conflict("Checkout.InsufficientStock", $"Insufficient stock for product '{item.Product!.Name}'."));
                }
            }

            var address = new Address(
                request.ShippingAddress.Street,
                request.ShippingAddress.City,
                request.ShippingAddress.State,
                request.ShippingAddress.PostalCode,
                request.ShippingAddress.Country);

            var orderItemsData = sortedItems.Select(i => (
                productId: i.ProductId,
                productName: i.Product!.Name,
                unitPrice: i.Product.Price,
                quantity: i.Quantity));

            var order = Order.Create(userId.Value, address, now, paymentDeadline, orderItemsData);
            _context.Orders.Add(order);

            // Clear cart: removing the children from the tracked collection deletes them
            // (required FK with cascade delete), so the explicit RemoveRange is redundant.
            cart.Clear(now);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Invalidate Redis catalog cache
            await _cacheService.IncrementVersionAsync("catalog:version", cancellationToken);

            var response = order.ToResponse(Array.Empty<PaymentSummaryResponse>());

            return Result<OrderResponse>.Success(response);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

// 2. Cancel Order Command
public sealed record CancelOrderCommand(Guid OrderId, string? Reason = null) : IRequest<Result>;

public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICacheService _cacheService;
    private readonly TimeProvider _timeProvider;

    public CancelOrderCommandHandler(
        IApplicationDbContext context,
        IInventoryService inventoryService,
        ICurrentUserService currentUserService,
        ICacheService cacheService,
        TimeProvider timeProvider)
    {
        _context = context;
        _inventoryService = inventoryService;
        _currentUserService = currentUserService;
        _cacheService = cacheService;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return Result.Failure(Error.Unauthorized("Auth.Unauthorized", "User is not authenticated."));
        }

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order == null)
        {
            return Result.Failure(Error.NotFound("Order.NotFound", "Order not found."));
        }

        var isAdmin = _currentUserService.IsInRole("Admin");
        if (!isAdmin && order.UserId != userId.Value)
        {
            return Result.Failure(Error.Forbidden("Order.Forbidden", "You do not have permission to cancel this order."));
        }

        if (order.Status != OrderStatus.Pending && order.Status != OrderStatus.Paid)
        {
            return Result.Failure(Error.BadRequest("Order.CannotCancel", $"Order cannot be cancelled in status '{order.Status}'."));
        }

        var now = _timeProvider.GetUtcNow();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            order.Cancel(request.Reason ?? "Cancelled by user", now);

            foreach (var item in order.Items)
            {
                await _inventoryService.ReleaseAsync(item.ProductId, item.Quantity, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await _cacheService.IncrementVersionAsync("catalog:version", cancellationToken);

            return Result.Success();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
