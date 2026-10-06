using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.UserId)
            .IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(o => o.TotalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(o => o.PaymentDeadline)
            .IsRequired();

        builder.Property(o => o.CancellationReason)
            .HasMaxLength(500);

        // PostgreSQL system column xmin as optimistic concurrency token
        builder.Property(o => o.RowVersion)
            .IsRowVersion();

        builder.OwnsOne(o => o.ShippingAddress, a =>
        {
            a.Property(p => p.Street).HasColumnName("ShippingStreet").HasMaxLength(200).IsRequired();
            a.Property(p => p.City).HasColumnName("ShippingCity").HasMaxLength(100).IsRequired();
            a.Property(p => p.State).HasColumnName("ShippingState").HasMaxLength(100).IsRequired();
            a.Property(p => p.PostalCode).HasColumnName("ShippingPostalCode").HasMaxLength(20).IsRequired();
            a.Property(p => p.Country).HasColumnName("ShippingCountry").HasMaxLength(100).IsRequired();
        });

        builder.HasIndex(o => new { o.UserId, o.CreatedAt });
        builder.HasIndex(o => new { o.Status, o.CreatedAt });

        // The expiration sweep filters Status == Pending && PaymentDeadline <= now every cycle;
        // without the deadline in the key it falls back to the Status prefix plus a filter.
        builder.HasIndex(o => new { o.Status, o.PaymentDeadline });

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.Payments)
            .WithOne(p => p.Order)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .IsRequired();

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.OrderId)
            .IsRequired();

        builder.Property(p => p.StripeSessionId)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(p => p.StripeSessionId)
            .IsUnique();

        builder.Property(p => p.CheckoutUrl)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.ExpiresAt)
            .IsRequired();

        builder.Property(p => p.StripePaymentIntentId)
            .HasMaxLength(255);

        // Partial unique index: only 1 Pending payment per order at any time
        builder.HasIndex(p => p.OrderId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'");
    }
}
