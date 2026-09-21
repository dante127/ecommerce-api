using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Orders.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Application.Features.Orders.Queries;

// 1. Get Orders Query (Paged)
public sealed record GetOrdersQuery(int Page = 1, int PageSize = 20) : IRequest<Result<PagedList<OrderSummaryResponse>>>;

public sealed class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, Result<PagedList<OrderSummaryResponse>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetOrdersQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PagedList<OrderSummaryResponse>>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return Result<PagedList<OrderSummaryResponse>>.Failure(
                Error.Unauthorized("Auth.Unauthorized", "User is not authenticated."));
        }

        var isAdmin = _currentUserService.IsInRole("Admin");

        var query = _context.Orders.AsNoTracking();
        if (!isAdmin)
        {
            query = query.Where(o => o.UserId == userId.Value);
        }

        query = query.OrderByDescending(o => o.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize is > 0 and <= 100 ? request.PageSize : 20;

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryResponse(
                o.Id,
                o.Status.ToString(),
                o.TotalAmount,
                o.Items.Count,
                o.PaymentDeadline,
                o.CreatedAt))
            .ToListAsync(cancellationToken);

        var pagedList = new PagedList<OrderSummaryResponse>(items, page, pageSize, totalCount);
        return Result<PagedList<OrderSummaryResponse>>.Success(pagedList);
    }
}

// 2. Get Order By Id Query (IDOR check)
public sealed record GetOrderByIdQuery(Guid Id) : IRequest<Result<OrderResponse>>;

public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, Result<OrderResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetOrderByIdQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<OrderResponse>> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return Result<OrderResponse>.Failure(
                Error.Unauthorized("Auth.Unauthorized", "User is not authenticated."));
        }

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

        if (order == null)
        {
            return Result<OrderResponse>.Failure(
                Error.NotFound("Order.NotFound", "Order not found."));
        }

        var isAdmin = _currentUserService.IsInRole("Admin");
        if (!isAdmin && order.UserId != userId.Value)
        {
            return Result<OrderResponse>.Failure(
                Error.Forbidden("Order.Forbidden", "You do not have permission to view this order."));
        }

        var response = new OrderResponse(
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
            order.Items.Select(i => new OrderItemResponse(
                i.ProductId,
                i.ProductName,
                i.UnitPrice,
                i.Quantity,
                i.UnitPrice * i.Quantity)).ToList(),
            order.Payments.Select(p => new PaymentSummaryResponse(
                p.Id,
                p.Status.ToString(),
                p.Amount,
                p.StripePaymentIntentId,
                p.CreatedAt)).ToList());

        return Result<OrderResponse>.Success(response);
    }
}
