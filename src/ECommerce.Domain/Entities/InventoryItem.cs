using ECommerce.Domain.Exceptions;

namespace ECommerce.Domain.Entities;

public sealed class InventoryItem
{
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Product? Product { get; private set; }

    private InventoryItem() { }

    public static InventoryItem Create(Guid productId, int initialQuantity, DateTimeOffset now)
    {
        if (initialQuantity < 0)
            throw new DomainException("Initial stock quantity cannot be negative.");

        return new InventoryItem
        {
            ProductId = productId,
            Quantity = initialQuantity,
            UpdatedAt = now
        };
    }

    public void SetQuantity(int quantity, DateTimeOffset now)
    {
        if (quantity < 0)
            throw new DomainException("Stock quantity cannot be negative.");

        Quantity = quantity;
        UpdatedAt = now;
    }
}
