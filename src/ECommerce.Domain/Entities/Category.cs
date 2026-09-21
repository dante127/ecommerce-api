using ECommerce.Domain.Common;
using ECommerce.Domain.Exceptions;

namespace ECommerce.Domain.Entities;

public sealed class Category : AggregateRoot<Guid>
{
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public Guid? ParentId { get; private set; }
    public bool IsActive { get; private set; }

    public Category? Parent { get; private set; }
    private readonly List<Category> _subCategories = new();
    public IReadOnlyCollection<Category> SubCategories => _subCategories.AsReadOnly();

    private Category() { }

    public static Category Create(string name, string slug, Guid? parentId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");

        if (string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Category slug is required.");

        return new Category
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            ParentId = parentId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(string name, string slug, Guid? parentId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");

        if (string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Category slug is required.");

        if (parentId.HasValue && parentId.Value == Id)
            throw new DomainException("A category cannot be its own parent.");

        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        ParentId = parentId;
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
}
