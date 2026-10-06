using System.Net;
using ECommerce.Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

public class ForwardedHeadersTrustConfigurationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ForwardedHeadersTrustConfigurationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ForwardedHeaders_WithNoTrustListConfigured_KeepTheLoopbackDefaults()
    {
        _factory.RequireContainers();

        var options = _factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        // With nothing configured, the framework defaults (loopback only) must survive; the defect
        // that was removed here is the unconditional Clear() of the trust lists.
        options.KnownProxies.Should().Contain(IPAddress.IPv6Loopback);
        options.KnownIPNetworks.Should().Contain(network => network.Contains(IPAddress.Loopback));
    }
}

public class ForwardedHeadersConfiguredTrustTests : IClassFixture<NoTrustForwardedHeadersFactory>
{
    private readonly NoTrustForwardedHeadersFactory _factory;

    public ForwardedHeadersConfiguredTrustTests(NoTrustForwardedHeadersFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ForwardedHeaders_WithATrustListConfigured_AreExactlyTheDeclaredEntries()
    {
        _factory.RequireContainers();

        var options = _factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        // The configured list is the complete trust statement: it must replace the loopback
        // defaults entirely and contain nothing else.
        options.KnownProxies.Should().ContainSingle(ip => ip.Equals(IPAddress.Parse("10.99.99.99")),
            $"actual proxies: {string.Join(", ", options.KnownProxies)}");
        options.KnownIPNetworks.Should().ContainSingle(network => network.ToString().StartsWith("192.0.2.0"),
            $"actual networks: {string.Join(", ", options.KnownIPNetworks)}");
    }
}

/// <summary>
/// Exercises ForwardedHeadersMiddleware directly with the application's configured trust posture.
/// The TestServer client address is always trusted by the framework (IPv6 loopback is hard-trusted
/// regardless of the configured lists), so header processing for untrusted senders cannot be
/// observed through the integration host; these tests drive the middleware in memory instead.
/// </summary>
public class ForwardedHeadersMiddlewareBehaviorTests
{
    // Mirrors what Program.cs builds from ForwardedHeaders:KnownProxies/KnownNetworks.
    private static ForwardedHeadersOptions CreateConfiguredOptions() => new()
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        KnownProxies = { IPAddress.Parse("10.99.99.99") },
        KnownIPNetworks = { System.Net.IPNetwork.Parse("192.0.2.0/24") }
    };

    private static async Task<HttpContext> InvokeMiddlewareAsync(
        ForwardedHeadersOptions options, IPAddress senderAddress, string? forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = senderAddress;
        if (forwardedFor is not null)
        {
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options));

        await middleware.Invoke(context);
        return context;
    }

    [Fact]
    public async Task ForwardedFor_FromAnUntrustedCaller_IsIgnored()
    {
        var context = await InvokeMiddlewareAsync(
            CreateConfiguredOptions(), IPAddress.Parse("203.0.113.5"), "198.51.100.9");

        // A caller that is not a declared proxy cannot choose the rate-limit partition it lands in.
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("203.0.113.5"));
    }

    [Fact]
    public async Task ForwardedFor_FromAConfiguredTrustedProxy_IsApplied()
    {
        // Under the previous always-cleared trust lists this proxy's headers were silently ignored,
        // so every client behind it shared one rate-limit partition and logged under the proxy IP.
        var context = await InvokeMiddlewareAsync(
            CreateConfiguredOptions(), IPAddress.Parse("10.99.99.99"), "198.51.100.9");

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("198.51.100.9"),
            "a declared proxy's X-Forwarded-For must resolve the real client address");
    }

    [Fact]
    public async Task ForwardedFor_FromLoopback_IsAlwaysTrustedByTheFramework()
    {
        // ASP.NET Core hard-trusts loopback addresses regardless of the configured lists. This
        // pins that semantics: the trust lists govern real proxy addresses; they cannot be used
        // to (nor relied upon to) restrict loopback senders.
        var context = await InvokeMiddlewareAsync(
            CreateConfiguredOptions(), IPAddress.Parse("127.0.0.1"), "198.51.100.9");

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("198.51.100.9"));
    }
}
