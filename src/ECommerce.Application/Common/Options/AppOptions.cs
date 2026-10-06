namespace ECommerce.Application.Common.Options;

/// <summary>
/// Token lifetimes, bound from the "Jwt" configuration section. The property names match the
/// existing keys so no configuration migration is needed; the defaults reproduce the values that
/// were previously hardcoded in the auth handlers and TokenService.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public int ExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}

/// <summary>
/// Payment lifecycle, bound from the "Payment" configuration section. The session/deadline pair
/// implements the ADR-009 safety gap: the Stripe session must close before the order deadline.
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    public int PaymentDeadlineMinutes { get; set; } = 35;
    public int CheckoutSessionMinutes { get; set; } = 31;
    public string Currency { get; set; } = "usd";
}

/// <summary>Catalogue display policy, bound from the "Catalog" configuration section.</summary>
public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    public int LowStockThreshold { get; set; } = 5;
}
