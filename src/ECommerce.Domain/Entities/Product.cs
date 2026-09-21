using ECommerce.Domain.Common;
using ECommerce.Domain.Exceptions;

namespace ECommerce.Domain.Entities;

public sealed class Product : AggregateRoot<Guid>
{
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public decimal Price { get; private set; }
    public Guid CategoryId { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }
    public uint RowVersion { get; private set; } // Maps to PostgreSQL xmin system column

    public Category Category { get; private set; } = null!;
    public InventoryItem? Inventory { get; private set; }

    private Product() { }

    public static Product Create(string sku, string name, string description, decimal price, Guid categoryId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sku)) throw new DomainException("SKU is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Product name is required.");
        if (price <= 0) throw new DomainException("Price must be greater than zero.");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = sku.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Price = price,
            CategoryId = categoryId,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = now
        };
    }

    public void UpdateDetails(string name, string description, Guid categoryId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Product name is required.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        CategoryId = categoryId;
        UpdatedAt = now;
    }

    public void UpdatePrice(decimal newPrice, DateTimeOffset now)
    {
        if (newPrice <= 0) throw new DomainException("Price must be greater than zero.");
        Price = newPrice;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    public void SoftDelete(DateTimeOffset now)
    {
        IsDeleted = true;
        IsActive = false;
        UpdatedAt = now;
    }
}
