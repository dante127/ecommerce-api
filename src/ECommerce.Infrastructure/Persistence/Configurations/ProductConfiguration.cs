using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", t =>
        {
            t.HasCheckConstraint("CK_Products_Price_Positive", "\"Price\" > 0");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(p => p.Sku)
            .IsUnique();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.Price)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .IsRequired();

        builder.Property(p => p.IsDeleted)
            .IsRequired();

        // PostgreSQL system column xmin as optimistic concurrency token
        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        // Soft delete global query filter
        builder.HasQueryFilter(p => !p.IsDeleted);

        // The catalogue filters on IsDeleted (through the global query filter) and on
        // IsActive, and supports a price range inside a category, so the index filter
        // mirrors those predicates instead of IsDeleted alone.
        builder.HasIndex(p => new { p.CategoryId, p.Price })
            .HasFilter("\"IsDeleted\" = false AND \"IsActive\" = true");

        // The trigram indexes that make the catalogue search indexable are functional indexes on
        // lower(Name) and lower(Description). EF has no fluent API for an index expression, so they
        // live in the AddTrigramSearchIndexes migration, next to the pg_trgm extension they need.
        // A leading-wildcard LIKE cannot use a B-tree index, so without them every uncached search
        // scanned the whole Products table.

        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Inventory)
            .WithOne(i => i.Product)
            .HasForeignKey<InventoryItem>(i => i.ProductId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems", t =>
        {
            t.HasCheckConstraint("CK_InventoryItems_Quantity_NonNegative", "\"Quantity\" >= 0");
        });

        builder.HasKey(i => i.ProductId);

        builder.Property(i => i.Quantity)
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
            .IsRequired();
    }
}
