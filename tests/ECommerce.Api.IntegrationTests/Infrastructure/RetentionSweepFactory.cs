using Microsoft.AspNetCore.Hosting;

namespace ECommerce.Api.IntegrationTests.Infrastructure;

/// <summary>Host whose retention sweep runs every second so the test can observe a purge.</summary>
public sealed class RetentionSweepFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("Retention:IntervalSeconds", "1");
    }
}
