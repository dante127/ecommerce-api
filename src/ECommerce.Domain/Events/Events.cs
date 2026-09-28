using ECommerce.Domain.Common;

namespace ECommerce.Domain.Events;

public sealed record OrderCreatedEvent(Guid OrderId, Guid UserId, decimal TotalAmount, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderPaidEvent(Guid OrderId, Guid UserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderCancelledEvent(Guid OrderId, Guid UserId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
