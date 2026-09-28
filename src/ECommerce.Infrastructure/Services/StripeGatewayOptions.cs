namespace ECommerce.Infrastructure.Services;

/// <summary>
/// Resolved Stripe gateway configuration. The mock decision is made exactly once, at
/// startup, because it depends on the hosting environment (see <c>AddInfrastructure</c>).
/// </summary>
public sealed record StripeGatewayOptions(bool UseMockGateway);
