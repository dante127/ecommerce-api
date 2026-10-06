using ECommerce.Application.Common.Authorization;
using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Common.Options;
using ECommerce.Application.Features.Payments.DTOs;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Application.Features.Payments.Commands;

// 1. Create Checkout Session Command
public sealed record CreateCheckoutSessionCommand(Guid OrderId) : IRequest<Result<CheckoutSessionResponse>>;

public sealed class CreateCheckoutSessionCommandHandler : IRequestHandler<CreateCheckoutSessionCommand, Result<CheckoutSessionResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentOptions _paymentOptions;
    private readonly ILogger<CreateCheckoutSessionCommandHandler> _logger;

    public CreateCheckoutSessionCommandHandler(
        IApplicationDbContext context,
        IPaymentGateway paymentGateway,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider,
        IOptions<PaymentOptions> paymentOptions,
        ILogger<CreateCheckoutSessionCommandHandler> logger)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
        _paymentOptions = paymentOptions.Value;
        _logger = logger;
    }

    public async Task<Result<CheckoutSessionResponse>> Handle(CreateCheckoutSessionCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return Result<CheckoutSessionResponse>.Failure(Error.Unauthenticated);
        }

        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order == null)
        {
            return Result<CheckoutSessionResponse>.Failure(
                Error.NotFound("Order.NotFound", "Order not found."));
        }

        var isAdmin = _currentUserService.IsInRole(UserRoles.Admin);
        if (!isAdmin && order.UserId != userId.Value)
        {
            return Result<CheckoutSessionResponse>.Failure(
                Error.Forbidden("Order.Forbidden", "You do not have permission to access this order."));
        }

        if (order.Status == OrderStatus.Paid)
        {
            return Result<CheckoutSessionResponse>.Failure(
                Error.BadRequest("Payment.OrderAlreadyPaid", "Order is already paid."));
        }

        if (order.Status != OrderStatus.Pending)
        {
            return Result<CheckoutSessionResponse>.Failure(
                Error.BadRequest("Payment.InvalidOrderStatus", $"Cannot create payment session for order in status '{order.Status}'."));
        }

        var now = _timeProvider.GetUtcNow();
        if (order.PaymentDeadline <= now)
        {
            return Result<CheckoutSessionResponse>.Failure(
                Error.BadRequest("Payment.OrderExpired", "The payment deadline for this order has expired."));
        }

        // Check for an active pending payment session to reuse
        var activePayment = order.Payments.FirstOrDefault(p => p.Status == PaymentStatus.Pending && p.ExpiresAt > now);
        if (activePayment != null)
        {
            _logger.LogInformation("Reusing existing active payment session {PaymentId} for order {OrderId}",
                activePayment.Id, order.Id);

            return Result<CheckoutSessionResponse>.Success(new CheckoutSessionResponse(
                activePayment.Id,
                activePayment.StripeSessionId,
                activePayment.CheckoutUrl,
                activePayment.ExpiresAt));
        }

        // Expire any outdated pending payment records
        foreach (var outdated in order.Payments.Where(p => p.Status == PaymentStatus.Pending && p.ExpiresAt <= now))
        {
            outdated.MarkExpired(now);
        }

        // Stripe requires session expiration to be at least 30 minutes in the future
        var expiresAt = now.AddMinutes(_paymentOptions.CheckoutSessionMinutes);

        var items = order.Items.Select(i => (
            i.ProductName,
            i.UnitPrice,
            i.Quantity)).ToList();

        var sessionResult = await _paymentGateway.CreateCheckoutSessionAsync(
            order.Id,
            order.TotalAmount,
            _paymentOptions.Currency,
            items,
            expiresAt,
            cancellationToken);

        var payment = Payment.Create(
            order.Id,
            sessionResult.SessionId,
            sessionResult.CheckoutUrl,
            order.TotalAmount,
            expiresAt,
            now);

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<CheckoutSessionResponse>.Success(new CheckoutSessionResponse(
            payment.Id,
            payment.StripeSessionId,
            payment.CheckoutUrl,
            payment.ExpiresAt));
    }
}

// 2. Process Webhook Command
public sealed record ProcessWebhookCommand(
    string EventId,
    string EventType,
    string? SessionId,
    string? PaymentIntentId) : IRequest<Result>;

public sealed class ProcessWebhookCommandHandler : IRequestHandler<ProcessWebhookCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IRefundProcessor _refundProcessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProcessWebhookCommandHandler> _logger;

    public ProcessWebhookCommandHandler(
        IApplicationDbContext context,
        IRefundProcessor refundProcessor,
        TimeProvider timeProvider,
        ILogger<ProcessWebhookCommandHandler> logger)
    {
        _context = context;
        _refundProcessor = refundProcessor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> Handle(ProcessWebhookCommand request, CancellationToken cancellationToken)
    {
        // 1. Webhook Idempotency Check: if already processed, return success immediately
        var alreadyProcessed = await _context.ProcessedWebhookEvents
            .AnyAsync(e => e.StripeEventId == request.EventId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation("Webhook event {EventId} was already processed. Skipping (idempotent).", request.EventId);
            return Result.Success();
        }

        var now = _timeProvider.GetUtcNow();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (request.EventType == "checkout.session.completed" && !string.IsNullOrEmpty(request.SessionId))
            {
                var payment = await _context.Payments
                    .Include(p => p.Order)
                    .FirstOrDefaultAsync(p => p.StripeSessionId == request.SessionId, cancellationToken);

                if (payment != null)
                {
                    // Late payment check: if order was cancelled in the meantime
                    if (payment.Order.Status == OrderStatus.Cancelled)
                    {
                        payment.MarkRequiresRefund(now, request.PaymentIntentId);
                        _logger.LogWarning(
                            "Order {OrderId} was cancelled before payment succeeded. Payment {PaymentId} marked as RequiresRefund.",
                            payment.OrderId, payment.Id);

                        // Auto-refund policy: the flag stays in place on failure or when disabled.
                        await _refundProcessor.AttemptRefundAsync(payment, now, cancellationToken);
                    }
                    else if (payment.Order.Status == OrderStatus.Pending)
                    {
                        payment.MarkSucceeded(now, request.PaymentIntentId);
                        payment.Order.MarkAsPaid(now);
                        _logger.LogInformation(
                            "Order {OrderId} successfully paid via Stripe session {SessionId}.",
                            payment.OrderId, request.SessionId);
                    }
                }
                else
                {
                    _logger.LogWarning("No payment found for completed session {SessionId}.", request.SessionId);
                }
            }
            else if (request.EventType == "checkout.session.expired" && !string.IsNullOrEmpty(request.SessionId))
            {
                var payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.StripeSessionId == request.SessionId, cancellationToken);

                if (payment != null && payment.Status == PaymentStatus.Pending)
                {
                    payment.MarkExpired(now);
                    _logger.LogInformation("Payment {PaymentId} marked as Expired for session {SessionId}.", payment.Id, request.SessionId);
                }
            }

            var processedEvent = ProcessedWebhookEvent.Create(request.EventId, request.EventType, now);
            _context.ProcessedWebhookEvents.Add(processedEvent);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to process webhook event {EventId}", request.EventId);
            throw;
        }
    }
}
