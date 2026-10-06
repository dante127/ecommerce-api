using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

/// <summary>
/// The retention sweep must purge only DEAD rows past their retention window: an expired token
/// old enough to be purged goes, but a still-active token (however old) and a recently expired
/// one stay - the former would log a user out, the latter erases the ADR-008 reuse-detection
/// signal. Processed webhook events follow the same shape.
/// </summary>
public class RetentionSweepTests : IClassFixture<RetentionSweepFactory>
{
    private readonly RetentionSweepFactory _factory;

    public RetentionSweepTests(RetentionSweepFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sweep_PurgesOnlyDeadRowsPastTheRetentionWindow()
    {
        _factory.RequireContainers();

        var oldDeadHash = $"hash_old_dead_{Guid.NewGuid():N}";
        var oldAliveHash = $"hash_old_alive_{Guid.NewGuid():N}";
        var recentDeadHash = $"hash_recent_dead_{Guid.NewGuid():N}";
        var oldEventId = $"evt_old_{Guid.NewGuid():N}";
        var freshEventId = $"evt_fresh_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Created 40 days ago and expired 33 days ago: past the 30-day window and dead - purged.
            db.RefreshTokens.Add(RefreshToken.Create(
                Guid.NewGuid(), Guid.NewGuid(), oldDeadHash, now.AddDays(-33), now.AddDays(-40)));
            // Created 40 days ago but still active: never purged while it lives.
            db.RefreshTokens.Add(RefreshToken.Create(
                Guid.NewGuid(), Guid.NewGuid(), oldAliveHash, now.AddDays(7), now.AddDays(-40)));
            // Expired seconds ago: dead, but well inside the window - kept for reuse detection.
            db.RefreshTokens.Add(RefreshToken.Create(
                Guid.NewGuid(), Guid.NewGuid(), recentDeadHash, now.AddSeconds(-1), now));

            db.ProcessedWebhookEvents.Add(ProcessedWebhookEvent.Create(
                oldEventId, "checkout.session.completed", now.AddDays(-100)));
            db.ProcessedWebhookEvents.Add(ProcessedWebhookEvent.Create(
                freshEventId, "checkout.session.completed", now));

            await db.SaveChangesAsync();
        }

        // Wait for the 1-second sweep to purge the eligible rows.
        var waitUntil = DateTime.UtcNow.AddSeconds(20);
        var purged = false;
        while (DateTime.UtcNow < waitUntil)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            purged = !await db.RefreshTokens.AnyAsync(t => t.TokenHash == oldDeadHash)
                     && !await db.ProcessedWebhookEvents.AnyAsync(e => e.StripeEventId == oldEventId);
            if (purged)
            {
                break;
            }

            await Task.Delay(250);
        }

        purged.Should().BeTrue("dead rows past the retention window must be purged by the sweep");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (await db.RefreshTokens.AnyAsync(t => t.TokenHash == oldAliveHash)).Should().BeTrue(
                "a still-active token must never be purged, however old");
            (await db.RefreshTokens.AnyAsync(t => t.TokenHash == recentDeadHash)).Should().BeTrue(
                "a recently expired token must be kept for reuse detection (ADR-008)");
            (await db.ProcessedWebhookEvents.AnyAsync(e => e.StripeEventId == freshEventId)).Should().BeTrue(
                "a recent processed webhook event must be kept for idempotency");
        }
    }
}
