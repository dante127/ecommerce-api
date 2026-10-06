using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using ECommerce.Api.Extensions;
using ECommerce.Api.Middleware;
using ECommerce.Application;
using ECommerce.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

// System.Net supplies IPAddress/IPNetwork for the forwarded-header trust list below.
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog Setup
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "ECommerce.Api")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);
    // Console sink + JSON formatter are configured once in appsettings.json (Serilog:WriteTo).
});

// Add Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

// 2. Controllers & JSON Options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// 3. API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddMvc()
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// 4. OpenAPI / Swagger
builder.Services.AddSwaggerDocumentation();

// 5. ProblemDetails & Global Exception Handler
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// 6. Forwarded Headers for Proxy Support
// Only proxies declared here may set client-identity headers. An undeclared caller's forwarded
// headers are ignored, so Connection.RemoteIpAddress stays the real socket address and the auth
// rate limiter cannot be partition-hopped by header spoofing. The loopback defaults apply until a
// deployment replaces them with its actual proxy list (see README: Deployment).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    var configuredProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
    if (configuredProxies is { Length: > 0 })
    {
        // The configured list is the complete truth: it replaces the loopback defaults entirely.
        options.KnownProxies.Clear();
        foreach (var entry in configuredProxies)
        {
            if (!IPAddress.TryParse(entry, out var proxy))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies entry '{entry}' is not a valid IP address. Configure it via ForwardedHeaders__KnownProxies__0.");
            }

            options.KnownProxies.Add(proxy);
        }
    }

    var configuredNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
    if (configuredNetworks is { Length: > 0 })
    {
        options.KnownIPNetworks.Clear();
        foreach (var entry in configuredNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(entry, out var network))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks entry '{entry}' is not a valid CIDR network (e.g. 10.0.0.0/8). Configure it via ForwardedHeaders__KnownNetworks__0.");
            }

            options.KnownIPNetworks.Add(network);
        }
    }
});

// 7. Rate Limiting
// Auth rate limiting. Limits are configuration-driven so a deployment - or a test host that
// registers many users from a single address - can adjust them without editing this file.
var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit") ?? 10;
var authWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Auth:WindowSeconds") ?? 60;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-rate-limit", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// 8. Health Checks
var postgresConnection = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Port=5432;Database=ecommerce;Username=postgres;Password=postgres";
var redisConnection = builder.Configuration.GetConnectionString("Redis") 
    ?? "localhost:6379";

builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConnection, name: "postgresql", tags: new[] { "ready" })
    .AddRedis(redisConnection, name: "redis", tags: new[] { "ready" });

// 9. TimeProvider (built-in .NET 10)
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// Apply migrations and seed reference data in Development, or wherever it has been
// explicitly enabled. Outside Development a deployment should run migrations itself.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await app.MigrateAndSeedAsync();
}

// Pipeline Configuration
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseForwardedHeaders();

app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerDocumentation();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Security Headers Middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");

    // Exclude Swagger UI from strict CSP restrictions
    if (!context.Request.Path.StartsWithSegments("/swagger"))
    {
        context.Response.Headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none';");
    }

    await next();
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health Check Endpoints
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // Liveness: basic web server responsiveness
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") // Readiness: DB & Redis connectivity
});

app.MapHealthChecks("/health");

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
