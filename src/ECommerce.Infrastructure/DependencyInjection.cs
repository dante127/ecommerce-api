using System.Text;
using ECommerce.Application.Common.Interfaces;
using ECommerce.Infrastructure.Identity;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace ECommerce.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool isDevelopment = false)
    {
        // 1. PostgreSQL & EF Core
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=ecommerce;Username=postgres;Password=postgres";

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // 2. ASP.NET Core Identity
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 8;
            options.User.RequireUniqueEmail = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // 3. JWT Authentication
        // Fail fast. The previous fallback meant a deployment that forgot Jwt__Key would
        // silently sign tokens with a key published in this repository.
        var jwtKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key is missing or shorter than 32 characters. Configure it via Jwt__Key " +
                "(environment variable or user-secrets) before starting the API.");
        }

        if (!isDevelopment &&
            jwtKey == "SuperSecretDevelopmentKeyForECommerceApiTestingOnlyMustBeLongerThan32Bytes!")
        {
            throw new InvalidOperationException(
                "Jwt:Key is still the development placeholder from appsettings.json. Configure a " +
                "real key via Jwt__Key before running outside Development.");
        }
        var jwtIssuer = configuration["Jwt:Issuer"] ?? "ECommerceApi";
        var jwtAudience = configuration["Jwt:Audience"] ?? "ECommerceClient";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew = TimeSpan.Zero
            };
        });

        // 4. Redis Caching
        var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "ECommerce_";
        });
        services.AddScoped<ICacheService, Caching.RedisCacheService>();

        // 5. Identity & Context Services
        services.AddHttpContextAccessor();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IInventoryService, InventoryService>();

        // Stripe. Mock mode is resolved once, here, because it depends on the hosting
        // environment: on by default in Development, never allowed anywhere else.
        var useMockStripe = configuration.GetValue<bool?>("Stripe:UseMockGateway") ?? isDevelopment;
        if (useMockStripe && !isDevelopment)
        {
            throw new InvalidOperationException(
                "Stripe:UseMockGateway must not be enabled outside Development. Configure a " +
                "real Stripe:SecretKey and Stripe:WebhookSecret instead.");
        }

        if (!useMockStripe &&
            (string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"]) ||
             string.IsNullOrWhiteSpace(configuration["Stripe:WebhookSecret"])))
        {
            throw new InvalidOperationException(
                "Stripe:SecretKey and Stripe:WebhookSecret are required when " +
                "Stripe:UseMockGateway is disabled. Set Stripe__SecretKey and " +
                "Stripe__WebhookSecret, or enable Stripe:UseMockGateway in Development.");
        }

        services.AddSingleton(new StripeGatewayOptions(useMockStripe));
        services.AddScoped<IPaymentGateway, StripePaymentGateway>();

        // 6. Background Services
        services.AddHostedService<BackgroundJobs.OrderExpirationBackgroundService>();

        return services;
    }
}
