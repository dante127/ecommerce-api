using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Slug)
            .IsRequired()
            .HasMaxLength(120);

        builder.HasIndex(c => c.Slug)
            .IsUnique();

        builder.HasOne(c => c.Parent)
            .WithMany(c => c.SubCategories)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId)
            .IsRequired();

        builder.HasIndex(c => c.UserId)
            .IsUnique();

        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems", t =>
        {
            t.HasCheckConstraint("CK_CartItems_Quantity_Positive", "\"Quantity\" > 0");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity)
            .IsRequired();

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
