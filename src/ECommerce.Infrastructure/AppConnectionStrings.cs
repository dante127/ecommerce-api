using Microsoft.Extensions.Configuration;

namespace ECommerce.Infrastructure;

/// <summary>
/// Single source for connection-string keys and their local-development fallbacks, so the health
/// checks and the EF/Redis registrations can never disagree about what they connect to.
/// </summary>
public static class AppConnectionStrings
{
    public const string PostgresKey = "DefaultConnection";
    public const string RedisKey = "Redis";
    public const string PostgresFallback = "Host=localhost;Port=5432;Database=ecommerce;Username=postgres;Password=postgres";
    public const string RedisFallback = "localhost:6379";

    public static string GetPostgres(IConfiguration configuration) =>
        configuration.GetConnectionString(PostgresKey) ?? PostgresFallback;

    public static string GetRedis(IConfiguration configuration) =>
        configuration.GetConnectionString(RedisKey) ?? RedisFallback;
}
