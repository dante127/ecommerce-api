using ECommerce.Domain.Common;
using ECommerce.Domain.Exceptions;

namespace ECommerce.Domain.Entities;

public sealed class ProcessedWebhookEvent : BaseEntity<Guid>
{
    public string StripeEventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public DateTimeOffset ProcessedAt { get; private set; }

    private ProcessedWebhookEvent() { }

    public static ProcessedWebhookEvent Create(string stripeEventId, string eventType, DateTimeOffset processedAt)
    {
        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new DomainException("StripeEventId is required.");

        if (string.IsNullOrWhiteSpace(eventType))
            throw new DomainException("EventType is required.");

        return new ProcessedWebhookEvent
        {
            Id = Guid.NewGuid(),
            StripeEventId = stripeEventId.Trim(),
            EventType = eventType.Trim(),
            ProcessedAt = processedAt,
            CreatedAt = processedAt
        };
    }
}
