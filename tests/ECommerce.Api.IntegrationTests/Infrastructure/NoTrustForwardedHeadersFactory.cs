using Microsoft.AspNetCore.Hosting;

namespace ECommerce.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Host whose forwarded-header trust list is configured to proxies that are NOT the test server's
/// loopback address, so the resolved options can be verified to contain exactly the declared
/// entries. Middleware behavior for trusted/untrusted senders is covered in-memory by
/// ForwardedHeadersMiddlewareBehaviorTests, because the TestServer client address is always
/// trusted by the framework.
/// </summary>
public sealed class NoTrustForwardedHeadersFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("ForwardedHeaders:KnownProxies:0", "10.99.99.99");
        builder.UseSetting("ForwardedHeaders:KnownNetworks:0", "192.0.2.0/24");
    }
}
