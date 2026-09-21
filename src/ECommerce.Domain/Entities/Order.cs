using ECommerce.Domain.Common;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Events;
using ECommerce.Domain.Exceptions;
using ECommerce.Domain.ValueObjects;

namespace ECommerce.Domain.Entities;

public sealed class Order : AggregateRoot<Guid>
{
    public Guid UserId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public Address ShippingAddress { get; private set; } = null!;
    public DateTimeOffset PaymentDeadline { get; private set; }
    public string? CancellationReason { get; private set; }
    public uint RowVersion { get; private set; } // Maps to PostgreSQL xmin system column

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private readonly List<Payment> _payments = new();
    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    private Order() { }

    public static Order Create(
        Guid userId,
        Address shippingAddress,
        DateTimeOffset now,
        DateTimeOffset paymentDeadline,
        IEnumerable<(Guid productId, string productName, decimal unitPrice, int quantity)> items)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");

        if (shippingAddress == null)
            throw new DomainException("Shipping address is required.");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ShippingAddress = shippingAddress,
            Status = OrderStatus.Pending,
            PaymentDeadline = paymentDeadline,
            CreatedAt = now
        };

        foreach (var (productId, productName, unitPrice, quantity) in items)
        {
            if (quantity <= 0)
                throw new DomainException($"Item '{productName}' has invalid quantity: {quantity}.");

            if (unitPrice <= 0)
                throw new DomainException($"Item '{productName}' has invalid unit price: {unitPrice}.");

            order._items.Add(new OrderItem(order.Id, productId, productName, unitPrice, quantity));
        }

        if (!order._items.Any())
            throw new DomainException("Order must contain at least one item.");

        order.TotalAmount = order._items.Sum(i => i.UnitPrice * i.Quantity);
        order.AddDomainEvent(new OrderCreatedEvent(order.Id, order.UserId, order.TotalAmount));

        return order;
    }

    public void MarkAsPaid(DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidStateTransitionException(Status, OrderStatus.Paid);

        Status = OrderStatus.Paid;
        UpdatedAt = now;
        AddDomainEvent(new OrderPaidEvent(Id, UserId));
    }

    public void StartProcessing(DateTimeOffset now)
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidStateTransitionException(Status, OrderStatus.Processing);

        Status = OrderStatus.Processing;
        UpdatedAt = now;
    }

    public void MarkAsShipped(DateTimeOffset now)
    {
        if (Status != OrderStatus.Processing)
            throw new InvalidStateTransitionException(Status, OrderStatus.Shipped);

        Status = OrderStatus.Shipped;
        UpdatedAt = now;
    }

    public void MarkAsDelivered(DateTimeOffset now)
    {
        if (Status != OrderStatus.Shipped)
            throw new InvalidStateTransitionException(Status, OrderStatus.Delivered);

        Status = OrderStatus.Delivered;
        UpdatedAt = now;
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending && Status != OrderStatus.Paid)
            throw new InvalidStateTransitionException(Status, OrderStatus.Cancelled);

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAt = now;
        AddDomainEvent(new OrderCancelledEvent(Id, UserId, reason));
    }
}

public sealed class OrderItem : BaseEntity<Guid>
{
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    public Product? Product { get; private set; }

    private OrderItem() { }

    internal OrderItem(Guid orderId, Guid productId, string productName, decimal unitPrice, int quantity)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName.Trim();
        UnitPrice = unitPrice;
        Quantity = quantity;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
