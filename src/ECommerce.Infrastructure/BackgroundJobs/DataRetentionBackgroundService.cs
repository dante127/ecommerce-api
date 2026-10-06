using ECommerce.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.BackgroundJobs;

/// <summary>
/// Purges dead rows that would otherwise grow without bound: refresh tokens that are revoked or
/// expired beyond the retention window, and processed Stripe webhook events beyond theirs. The
/// windows keep token-reuse detection (ADR-008) and the webhook audit trail intact long after
/// Stripe stops retrying (72 hours maximum), while capping table growth.
/// </summary>
public sealed class DataRetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataRetentionBackgroundService> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _refreshTokenRetention;
    private readonly TimeSpan _webhookEventRetention;

    public DataRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DataRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _checkInterval = TimeSpan.FromSeconds(configuration.GetValue("Retention:IntervalSeconds", 3600));
        _refreshTokenRetention = TimeSpan.FromDays(configuration.GetValue("Retention:RefreshTokenDays", 30));
        _webhookEventRetention = TimeSpan.FromDays(configuration.GetValue("Retention:WebhookEventDays", 90));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataRetentionBackgroundService started.");

        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while purging expired data.");
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

        _logger.LogInformation("DataRetentionBackgroundService stopped.");
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        // Only dead tokens (revoked or expired) past the retention window are removed. Purging a
        // still-active token would log the user out; purging a recent dead one would erase the
        // reuse-detection signal that revokes the whole family (ADR-008).
        var tokenCutoff = now - _refreshTokenRetention;
        var tokensPurged = await context.RefreshTokens
            .Where(t => t.CreatedAt < tokenCutoff && (t.RevokedAt != null || t.ExpiresAt < now))
            .ExecuteDeleteAsync(cancellationToken);

        var webhookCutoff = now - _webhookEventRetention;
        var eventsPurged = await context.ProcessedWebhookEvents
            .Where(e => e.ProcessedAt < webhookCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (tokensPurged > 0 || eventsPurged > 0)
        {
            _logger.LogInformation(
                "Retention sweep purged {RefreshTokens} refresh token(s) and {WebhookEvents} processed webhook event(s).",
                tokensPurged, eventsPurged);
        }
    }
}
