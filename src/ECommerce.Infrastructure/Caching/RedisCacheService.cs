using System.Text.Json;
using ECommerce.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ECommerce.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _distributedCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(
        IDistributedCache distributedCache,
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger)
    {
        _distributedCache = distributedCache;
        _redis = redis;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var cachedData = await _distributedCache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrEmpty(cachedData))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(cachedData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve cache key '{Key}' from Redis. Falling back to database.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(value);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromMinutes(5)
            };

            await _distributedCache.SetStringAsync(key, serialized, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set cache key '{Key}' in Redis.", key);
        }
    }

    public async Task<long> IncrementVersionAsync(string versionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var newVersion = await db.StringIncrementAsync(versionKey);
            _logger.LogInformation("Incremented Redis cache version for key '{Key}' to {Version}", versionKey, newVersion);
            return newVersion;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to increment version for cache key '{Key}'.", versionKey);
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    public async Task<long> GetVersionAsync(string versionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(versionKey);
            if (value.HasValue && long.TryParse((string?)value, out var version))
            {
                return version;
            }

            // A missing counter reports 0, NOT 1: the first invalidation INCRs the counter to 1,
            // and that must differ from the value reads used before any invalidation, otherwise
            // the first-ever cache invalidation would leave the pre-existing entries valid.
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get version for cache key '{Key}'. Defaulting to 0.", versionKey);
            return 0;
        }
    }
}
