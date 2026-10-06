namespace ECommerce.Domain.Common;

public abstract class BaseEntity<TId>
{
    public TId Id { get; protected set; } = default!;
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset? UpdatedAt { get; protected set; }
}

/// <summary>
/// Marker for aggregate roots. Domain events are deliberately absent: nothing dispatches them, so
/// collecting them would imply a subscriber that does not exist. ADR-010 introduces the seam
/// together with the transactional outbox and its first real consumer.
/// </summary>
public abstract class AggregateRoot<TId> : BaseEntity<TId>
{
}
