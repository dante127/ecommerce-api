using ECommerce.Domain.Common;

namespace ECommerce.Domain.Events;

public sealed record OrderCreatedEvent(Guid OrderId, Guid UserId, decimal TotalAmount) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

public sealed record OrderPaidEvent(Guid OrderId, Guid UserId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

public sealed record OrderCancelledEvent(Guid OrderId, Guid UserId, string Reason) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
