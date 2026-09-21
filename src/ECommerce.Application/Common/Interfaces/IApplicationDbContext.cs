using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ECommerce.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Product> Products { get; }
    DbSet<InventoryItem> InventoryItems { get; }
    DbSet<Category> Categories { get; }
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
