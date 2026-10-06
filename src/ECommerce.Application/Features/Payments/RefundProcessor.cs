using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Options;
using ECommerce.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Application.Features.Payments;

/// <summary>
/// Executes the automatic refund policy for payments flagged RequiresRefund. Failure to refund
/// leaves the flag in place so the money state stays visible for follow-up; it never throws into
/// the caller's transaction.
/// </summary>
internal sealed class RefundProcessor : IRefundProcessor
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly PaymentOptions _paymentOptions;
    private readonly ILogger<RefundProcessor> _logger;

    public RefundProcessor(
        IPaymentGateway paymentGateway,
        IOptions<PaymentOptions> paymentOptions,
        ILogger<RefundProcessor> logger)
    {
        _paymentGateway = paymentGateway;
        _paymentOptions = paymentOptions.Value;
        _logger = logger;
    }

    public async Task AttemptRefundAsync(Payment payment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_paymentOptions.AutoRefund)
        {
            _logger.LogInformation(
                "Payment {PaymentId} stays flagged RequiresRefund: auto-refund is disabled.", payment.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(payment.StripePaymentIntentId))
        {
            _logger.LogError(
                "Payment {PaymentId} is flagged RequiresRefund but has no payment intent id; refund needs manual handling.",
                payment.Id);
            return;
        }

        try
        {
            var refunded = await _paymentGateway.TryRefundAsync(payment.StripePaymentIntentId, cancellationToken);
            if (refunded)
            {
                payment.MarkRefunded(now);
                _logger.LogInformation("Payment {PaymentId} was refunded automatically.", payment.Id);
            }
            else
            {
                _logger.LogError(
                    "Automatic refund failed for payment {PaymentId}; it stays flagged RequiresRefund.", payment.Id);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Automatic refund errored for payment {PaymentId}; it stays flagged RequiresRefund.", payment.Id);
        }
    }
}
