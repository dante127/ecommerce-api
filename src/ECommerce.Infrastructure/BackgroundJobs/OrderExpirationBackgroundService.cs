using ECommerce.Application.Common.Interfaces;
using ECommerce.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed class OrderExpirationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderExpirationBackgroundService> _logger;
    private readonly TimeSpan _checkInterval;

    public OrderExpirationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OrderExpirationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        // Configuration-driven so a test host can shorten the sweep cadence without code changes.
        _checkInterval = TimeSpan.FromSeconds(configuration.GetValue("OrderExpiration:IntervalSeconds", 60));
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
        // Select only ids. Holding tracked entities across the batch is the hazard: a failed
        // SaveChangesAsync leaves that entity Modified in the change tracker, and every later
        // SaveChangesAsync in the batch would re-emit and re-fail its UPDATE, blocking all
        // remaining orders until restart.
        List<Guid> expiredOrderIds;
        using (var selectionScope = _scopeFactory.CreateScope())
        {
            var context = selectionScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var timeProvider = selectionScope.ServiceProvider.GetRequiredService<TimeProvider>();
            var now = timeProvider.GetUtcNow();

            // Oldest deadline first. Without an ORDER BY the selection is arbitrary, so a backlog
            // larger than the batch size can starve some orders indefinitely.
            expiredOrderIds = await context.Orders
                .AsNoTracking()
                .Where(o => o.Status == OrderStatus.Pending && o.PaymentDeadline <= now)
                .OrderBy(o => o.PaymentDeadline)
                .ThenBy(o => o.Id)
                .Select(o => o.Id)
                .Take(50)
                .ToListAsync(cancellationToken);
        }

        if (expiredOrderIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} expired pending orders to cancel.", expiredOrderIds.Count);

        var cancelledCount = 0;
        foreach (var orderId in expiredOrderIds)
        {
            // One scope — and therefore one DbContext — per order, so a failure can never leak
            // tracked state into another order's transaction. Concurrent API instances are
            // tolerated: Order.RowVersion (PostgreSQL xmin) makes a losing writer surface
            // DbUpdateConcurrencyException, which fails only this order.
            await using var scope = _scopeFactory.CreateAsyncScope();
            try
            {
                var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
                var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
                var now = timeProvider.GetUtcNow();

                // Re-check state at processing time: the order may have been paid or cancelled
                // since selection. xmin still guards the write, but this avoids a doomed transaction.
                var order = await context.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId
                        && o.Status == OrderStatus.Pending
                        && o.PaymentDeadline <= now, cancellationToken);

                if (order == null)
                {
                    continue;
                }

                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The uncommitted transaction, if any, is rolled back by its own disposal.
                _logger.LogError(ex, "Failed to cancel expired order {OrderId}", orderId);
            }
        }

        if (cancelledCount > 0)
        {
            using var cacheScope = _scopeFactory.CreateScope();
            var cacheService = cacheScope.ServiceProvider.GetRequiredService<ICacheService>();
            await cacheService.IncrementVersionAsync("catalog:version", cancellationToken);
        }
    }
}
