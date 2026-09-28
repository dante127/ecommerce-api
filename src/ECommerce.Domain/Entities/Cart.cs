using ECommerce.Domain.Common;
using ECommerce.Domain.Exceptions;

namespace ECommerce.Domain.Entities;

public sealed class Cart : AggregateRoot<Guid>
{
    public Guid UserId { get; private set; }

    private readonly List<CartItem> _items = new();
    public IReadOnlyCollection<CartItem> Items => _items.AsReadOnly();

    private Cart() { }

    public static Cart Create(Guid userId, DateTimeOffset now)
    {
        return new Cart
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = now
        };
    }

    public void AddItem(Guid productId, int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
        {
            existingItem.AddQuantity(quantity, now);
        }
        else
        {
            _items.Add(new CartItem(Id, productId, quantity, now));
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// Quantity of <paramref name="productId"/> the cart would hold after adding
    /// <paramref name="additionalQuantity"/> more units.
    /// </summary>
    public int ProjectedQuantity(Guid productId, int additionalQuantity)
    {
        var existingQuantity = _items.FirstOrDefault(i => i.ProductId == productId)?.Quantity ?? 0;
        return existingQuantity + additionalQuantity;
    }

    public void UpdateItemQuantity(Guid productId, int quantity, DateTimeOffset now)
    {
        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem == null)
            throw new DomainException($"Product '{productId}' is not in the cart.");

        if (quantity <= 0)
        {
            _items.Remove(existingItem);
        }
        else
        {
            existingItem.SetQuantity(quantity, now);
        }

        UpdatedAt = now;
    }

    public void RemoveItem(Guid productId, DateTimeOffset now)
    {
        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
        {
            _items.Remove(existingItem);
            UpdatedAt = now;
        }
    }

    public void Clear(DateTimeOffset now)
    {
        _items.Clear();
        UpdatedAt = now;
    }
}

public sealed class CartItem : BaseEntity<Guid>
{
    public Guid CartId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }

    public Product? Product { get; private set; }

    private CartItem() { }

    internal CartItem(Guid cartId, Guid productId, int quantity, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CartId = cartId;
        ProductId = productId;
        Quantity = quantity;
        CreatedAt = now;
    }

    internal void AddQuantity(int quantity, DateTimeOffset now)
    {
        Quantity += quantity;
        UpdatedAt = now;
    }

    internal void SetQuantity(int quantity, DateTimeOffset now)
    {
        Quantity = quantity;
        UpdatedAt = now;
    }
}
