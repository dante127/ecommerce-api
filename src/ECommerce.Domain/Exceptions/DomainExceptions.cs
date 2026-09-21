using ECommerce.Domain.Enums;

namespace ECommerce.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class InvalidStateTransitionException : DomainException
{
    public OrderStatus CurrentStatus { get; }
    public OrderStatus TargetStatus { get; }

    public InvalidStateTransitionException(OrderStatus currentStatus, OrderStatus targetStatus)
        : base($"Invalid order state transition from '{currentStatus}' to '{targetStatus}'.")
    {
        CurrentStatus = currentStatus;
        TargetStatus = targetStatus;
    }
}

public class InsufficientStockException : DomainException
{
    public Guid ProductId { get; }
    public int RequestedQuantity { get; }
    public int AvailableQuantity { get; }

    public InsufficientStockException(Guid productId, int requestedQuantity, int availableQuantity)
        : base($"Insufficient stock for product '{productId}'. Requested: {requestedQuantity}, Available: {availableQuantity}.")
    {
        ProductId = productId;
        RequestedQuantity = requestedQuantity;
        AvailableQuantity = availableQuantity;
    }
}
