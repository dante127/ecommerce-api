namespace ECommerce.Domain.Common;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public abstract class BaseEntity<TId>
{
    public TId Id { get; protected set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public abstract class AggregateRoot<TId> : BaseEntity<TId>
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
