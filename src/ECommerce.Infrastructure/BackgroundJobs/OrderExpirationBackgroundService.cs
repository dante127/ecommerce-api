using ECommerce.Application.Common.Interfaces;
using ECommerce.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed class OrderExpirationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderExpirationBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(60);

    public OrderExpirationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderExpirationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderExpirationBackgroundService started.");

        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing expired orders.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("OrderExpirationBackgroundService stopped.");
    }

    private async Task ProcessExpiredOrdersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var now = timeProvider.GetUtcNow();

        var expiredOrders = await context.Orders
            .Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Pending && o.PaymentDeadline <= now)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!expiredOrders.Any())
        {
            return;
        }

        _logger.LogInformation("Found {Count} expired pending orders to cancel.", expiredOrders.Count);

        var cancelledCount = 0;
        foreach (var order in expiredOrders)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                order.Cancel("Payment deadline expired", now);

                foreach (var item in order.Items)
                {
                    await inventoryService.ReleaseAsync(item.ProductId, item.Quantity, cancellationToken);
                }

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                cancelledCount++;

                _logger.LogInformation("Order {OrderId} expired and was automatically cancelled. Stock released.", order.Id);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to cancel expired order {OrderId}", order.Id);
            }
        }

        if (cancelledCount > 0)
        {
            await cacheService.IncrementVersionAsync("catalog:version", cancellationToken);
        }
    }
}
