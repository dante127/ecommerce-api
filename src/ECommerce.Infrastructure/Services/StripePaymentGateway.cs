using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace ECommerce.Infrastructure.Services;

public sealed class StripePaymentGateway : IPaymentGateway
{
    private readonly bool _useMockGateway;
    private readonly string _secretKey;
    private readonly string _webhookSecret;
    private readonly PaymentOptions _paymentOptions;
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(
        IConfiguration configuration,
        StripeGatewayOptions options,
        IOptions<PaymentOptions> paymentOptions,
        ILogger<StripePaymentGateway> logger)
    {
        _useMockGateway = options.UseMockGateway;
        _secretKey = configuration["Stripe:SecretKey"] ?? string.Empty;
        _webhookSecret = configuration["Stripe:WebhookSecret"] ?? string.Empty;
        _paymentOptions = paymentOptions.Value;
        _logger = logger;
    }

    public async Task<PaymentSessionResult> CreateCheckoutSessionAsync(
        Guid orderId,
        decimal amount,
        string currency,
        IReadOnlyCollection<(string Name, decimal UnitPrice, int Quantity)> items,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        // Mock mode is decided once at startup (Stripe:UseMockGateway, Development default).
        if (_useMockGateway)
        {
            _logger.LogWarning(
                "Using mock Stripe checkout session for order {OrderId}. This must never be enabled outside Development.",
                orderId);
            var mockSessionId = $"cs_test_{Guid.NewGuid():N}";
            var mockUrl = $"https://checkout.stripe.com/pay/{mockSessionId}";
            return new PaymentSessionResult(mockSessionId, mockUrl);
        }

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = items.Select(item => new SessionLineItemOptions
            {
                PriceData = new SessionLineItemPriceDataOptions
                {
                    UnitAmount = (long)(item.UnitPrice * 100), // Stripe expects amounts in cents
                    Currency = currency.ToLowerInvariant(),
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = item.Name
                    }
                },
                Quantity = item.Quantity
            }).ToList(),
            Mode = "payment",
            SuccessUrl = _paymentOptions.SuccessUrl,
            CancelUrl = _paymentOptions.CancelUrl,
            ExpiresAt = expiresAt.UtcDateTime,
            Metadata = new Dictionary<string, string>
            {
                { "order_id", orderId.ToString() }
            }
        };

        // Credentials travel with the request instead of the process-global StripeConfiguration.ApiKey,
        // which a scoped service must never mutate: it is a last-write-wins race and a
        // cross-tenant/cross-test hazard.
        // The idempotency key makes session creation retry-safe: if the API call succeeds but the
        // process dies before the Payment row is persisted, the customer's retry (or the order's
        // next attempt) receives the SAME Stripe session instead of a second chargeable one.
        var requestOptions = new RequestOptions
        {
            ApiKey = _secretKey,
            IdempotencyKey = $"checkout-session-{orderId}"
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, requestOptions, cancellationToken);

        return new PaymentSessionResult(session.Id, session.Url);
    }

    public WebhookEventResult VerifyAndParseWebhook(string payload, string signatureHeader)
    {
        if (_useMockGateway)
        {
            // There is no real Stripe secret in mock mode, so nothing can be verified.
            // The payload shape mirrors the checkout.session.* events sent by the tests.
            return ParseUnverifiedWebhook(payload);
        }

        var stripeEvent = EventUtility.ConstructEvent(
            payload,
            signatureHeader,
            _webhookSecret,
            throwOnApiVersionMismatch: false);

        string? sessionId = null;
        string? paymentIntentId = null;
        string? orderIdFromMetadata = null;

        if (stripeEvent.Data.Object is Session session)
        {
            sessionId = session.Id;
            paymentIntentId = session.PaymentIntentId;
            orderIdFromMetadata = session.Metadata.TryGetValue("order_id", out var metadataOrderId) ? metadataOrderId : null;
        }
        else if (stripeEvent.Data.Object is PaymentIntent paymentIntent)
        {
            paymentIntentId = paymentIntent.Id;
            orderIdFromMetadata = paymentIntent.Metadata.TryGetValue("order_id", out var intentOrderId) ? intentOrderId : null;
        }

        return new WebhookEventResult(
            stripeEvent.Id,
            stripeEvent.Type,
            sessionId,
            paymentIntentId,
            orderIdFromMetadata);
    }

    public async Task<bool> TryRefundAsync(string paymentIntentId, CancellationToken cancellationToken = default)
    {
        if (_useMockGateway)
        {
            _logger.LogInformation("Mock Stripe refund accepted for payment intent {PaymentIntentId}.", paymentIntentId);
            return true;
        }

        try
        {
            // A full refund needs only the intent: Stripe uses the payment's original amount and
            // currency, which cannot drift from what the customer actually paid.
            var service = new RefundService();
            await service.CreateAsync(
                new RefundCreateOptions { PaymentIntent = paymentIntentId },
                new RequestOptions { ApiKey = _secretKey },
                cancellationToken);
            return true;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe refund failed for payment intent {PaymentIntentId}.", paymentIntentId);
            return false;
        }
    }

    private static WebhookEventResult ParseUnverifiedWebhook(string payload)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var id = root.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
        var type = root.GetProperty("type").GetString() ?? "unknown";
        string? sessionId = null;
        string? paymentIntentId = null;
        string? orderIdFromMetadata = null;

        if (root.TryGetProperty("data", out var data) && data.TryGetProperty("object", out var obj))
        {
            if (obj.TryGetProperty("id", out var objId)) sessionId = objId.GetString();
            if (obj.TryGetProperty("payment_intent", out var pi)) paymentIntentId = pi.GetString();
            if (obj.TryGetProperty("metadata", out var metadata) &&
                metadata.TryGetProperty("order_id", out var metadataOrderId))
            {
                orderIdFromMetadata = metadataOrderId.GetString();
            }
        }

        return new WebhookEventResult(id, type, sessionId, paymentIntentId, orderIdFromMetadata);
    }
}
