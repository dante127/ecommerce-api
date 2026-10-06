using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.ValueObjects;
using ECommerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

/// <summary>
/// A concurrency conflict while cancelling one expired order (a webhook paying it at the same
/// moment) must fail that order alone. Against the previous single-context worker, the failed
/// order stayed Modified in the shared change tracker and every subsequent order's SaveChangesAsync
/// re-emitted and re-failed it, permanently stalling the sweep. This test pins the isolation.
/// </summary>
public class ExpirationWorkerIsolationTests : IClassFixture<ExpirationWorkerIsolationFactory>
{
    private readonly ExpirationWorkerIsolationFactory _factory;

    public ExpirationWorkerIsolationTests(ExpirationWorkerIsolationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sweep_WhenOneExpiredOrderFailsToSave_StillCancelsTheOthers()
    {
        _factory.RequireContainers();

        Guid productId;
        Guid orderAId;
        Guid orderBId;
        const int initialStock = 10;
        const int quantityPerOrder = 2;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var injection = scope.ServiceProvider.GetRequiredService<OrderFailureInjection>();
            var now = DateTimeOffset.UtcNow;

            var category = await db.Categories.FirstAsync();
            var product = Product.Create(
                $"TECH-EXP-{Guid.NewGuid():N}".Substring(0, 20),
                "Expiration Isolation Item",
                "Product for the expiration-worker isolation test",
                50m,
                category.Id,
                now);
            db.Products.Add(product);
            db.InventoryItems.Add(InventoryItem.Create(product.Id, initialStock, now));

            // Each aggregate owns its own Address instance: one owned-entity instance cannot be
            // tracked under two parents.
            var orderA = Order.Create(Guid.NewGuid(), new Address("Street 1", "City", "State", "12345", "Country"),
                now, now.AddMinutes(-2), new[]
            {
                (product.Id, product.Name, product.Price, quantityPerOrder)
            });
            var orderB = Order.Create(Guid.NewGuid(), new Address("Street 2", "City", "State", "12345", "Country"),
                now, now.AddMinutes(-1), new[]
            {
                (product.Id, product.Name, product.Price, quantityPerOrder)
            });

            productId = product.Id;
            orderAId = orderA.Id;
            orderBId = orderB.Id;

            // Mark the failing order BEFORE the rows commit, so the sweep can never observe the
            // orders without the injection in place.
            injection.FailingOrderId = orderA.Id;

            db.Orders.AddRange(orderA, orderB);
            await db.SaveChangesAsync();
        }

        // Wait for the 1-second sweep to process the batch.
        var waitUntil = DateTime.UtcNow.AddSeconds(20);
        Order? processedOrderB = null;
        while (DateTime.UtcNow < waitUntil)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            processedOrderB = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderBId);
            if (processedOrderB is { Status: OrderStatus.Cancelled })
            {
                break;
            }

            await Task.Delay(250);
        }

        processedOrderB.Should().NotBeNull("the worker must have processed the batch");
        processedOrderB!.Status.Should().Be(OrderStatus.Cancelled,
            "a failing order must not poison the change tracker and block the orders behind it");
        processedOrderB.CancellationReason.Should().Be("Payment deadline expired");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var orderA = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderAId);
            orderA.Status.Should().Be(OrderStatus.Pending,
                "the injected conflict must leave the failing order untouched, ready for a later sweep");

            var stock = await db.InventoryItems
                .Where(i => i.ProductId == productId)
                .Select(i => i.Quantity)
                .FirstAsync();
            stock.Should().Be(initialStock + quantityPerOrder,
                "only the successfully cancelled order releases its stock; the failing order's rollback must undo its release");

            // Stop failing the sweep so the worker's background retries converge.
            scope.ServiceProvider.GetRequiredService<OrderFailureInjection>().FailingOrderId = null;
        }
    }
}
