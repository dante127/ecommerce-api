using System.Text;
using System.Text.Json;
using Asp.Versioning;
using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Features.Payments.Commands;
using ECommerce.Application.Features.Payments.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Stripe;

namespace ECommerce.Api.Controllers.v1;

[ApiVersion("1.0")]
public sealed class PaymentsController : BaseApiController
{
    // Stripe webhook payloads are a few KB; cap this anonymous endpoint's body size.
    private const long WebhookMaxBodyBytes = 262_144; // 256 KB

    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentGateway paymentGateway,
        ILogger<PaymentsController> logger,
        ISender sender)
        : base(sender)
    {
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    [HttpPost("checkout/{orderId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(CheckoutSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateCheckoutSession(Guid orderId, CancellationToken cancellationToken)
    {
        var command = new CreateCheckoutSessionCommand(orderId);
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    [RequestSizeLimit(WebhookMaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        // Cap the body so an anonymous caller cannot make the process buffer an
        // arbitrarily large request before the signature is even checked.
        using var reader = new StreamReader(
            HttpContext.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        var json = await reader.ReadToEndAsync(cancellationToken);
        var signatureHeader = Request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return BadRequest("Missing Stripe-Signature header.");
        }

        WebhookEventResult webhookEvent;
        try
        {
            webhookEvent = _paymentGateway.VerifyAndParseWebhook(json, signatureHeader);
        }
        catch (Exception ex) when (ex is StripeException or JsonException or KeyNotFoundException)
        {
            // Signature and payload problems are the only 400s here. Anything else is
            // an infrastructure failure and must reach the global handler as a 5xx so
            // Stripe retries it instead of treating the request as malformed.
            _logger.LogWarning(ex, "Rejected Stripe webhook: signature or payload could not be verified.");
            return BadRequest(new { error = "Invalid webhook signature or payload." });
        }

        var command = new ProcessWebhookCommand(
            webhookEvent.EventId,
            webhookEvent.EventType,
            webhookEvent.SessionId,
            webhookEvent.PaymentIntentId,
            webhookEvent.OrderIdFromMetadata);

        var result = await Sender.Send(command, cancellationToken);

        // Match the declared response type and the integration test: success is 200.
        return result.IsSuccess ? Ok() : HandleResult(result);
    }
}
