using ECommerce.Application.Common.Interfaces;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Holds the id of the order whose SaveChangesAsync must fail. Mutable at runtime so the test can
/// mark an order before its rows become visible to the background worker.
/// </summary>
public sealed class OrderFailureInjection
{
    public Guid? FailingOrderId { get; set; }
}

/// <summary>
/// Delegating IApplicationDbContext whose SaveChangesAsync throws a concurrency exception while the
/// marked order is tracked as Modified — the same shape a lost xmin race produces. Against the old
/// single-context worker this poisons every later order in the batch; against the per-order scoped
/// worker it fails only the marked order.
/// </summary>
public sealed class OrderCancellationFailingDbContext : IApplicationDbContext
{
    private readonly ApplicationDbContext _inner;
    private readonly OrderFailureInjection _injection;

    public OrderCancellationFailingDbContext(ApplicationDbContext inner, OrderFailureInjection injection)
    {
        _inner = inner;
        _injection = injection;
    }

    public DbSet<Product> Products => _inner.Products;
    public DbSet<InventoryItem> InventoryItems => _inner.InventoryItems;
    public DbSet<Category> Categories => _inner.Categories;
    public DbSet<Cart> Carts => _inner.Carts;
    public DbSet<CartItem> CartItems => _inner.CartItems;
    public DbSet<Order> Orders => _inner.Orders;
    public DbSet<OrderItem> OrderItems => _inner.OrderItems;
    public DbSet<Payment> Payments => _inner.Payments;
    public DbSet<RefreshToken> RefreshTokens => _inner.RefreshTokens;
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => _inner.ProcessedWebhookEvents;

    public DatabaseFacade Database => _inner.Database;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_injection.FailingOrderId is { } failingId &&
            _inner.ChangeTracker.Entries<Order>()
                .Any(entry => entry.Entity.Id == failingId && entry.State == EntityState.Modified))
        {
            throw new DbUpdateConcurrencyException(
                "Injected failure: the marked order was concurrently modified (simulated xmin conflict).");
        }

        return await _inner.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Host for the expiration-worker isolation test: the sweep runs every second and the application
/// DbContext is wrapped so that saving the marked order always fails.
/// </summary>
public sealed class ExpirationWorkerIsolationFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("OrderExpiration:IntervalSeconds", "1");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<OrderFailureInjection>();
            services.AddScoped<IApplicationDbContext>(sp => new OrderCancellationFailingDbContext(
                sp.GetRequiredService<ApplicationDbContext>(),
                sp.GetRequiredService<OrderFailureInjection>()));
        });
    }
}
