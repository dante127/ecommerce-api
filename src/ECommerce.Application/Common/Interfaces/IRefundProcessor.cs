using ECommerce.Domain.Entities;

namespace ECommerce.Application.Common.Interfaces;

/// <summary>
/// Applies the automatic refund policy to a payment flagged RequiresRefund (auto-refund with a
/// configuration kill-switch). Mutates the passed payment entity; the caller persists it.
/// </summary>
public interface IRefundProcessor
{
    Task AttemptRefundAsync(Payment payment, DateTimeOffset now, CancellationToken cancellationToken);
}
