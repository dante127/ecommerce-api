using Asp.Versioning;
using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Features.Payments.Commands;
using ECommerce.Application.Features.Payments.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Api.Controllers.v1;

[ApiVersion("1.0")]
public sealed class PaymentsController : BaseApiController
{
    private readonly IPaymentGateway _paymentGateway;

    public PaymentsController(IPaymentGateway paymentGateway)
    {
        _paymentGateway = paymentGateway;
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync(cancellationToken);
        var signatureHeader = Request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return BadRequest("Missing Stripe-Signature header.");
        }

        try
        {
            var webhookEvent = _paymentGateway.VerifyAndParseWebhook(json, signatureHeader);
            var command = new ProcessWebhookCommand(
                webhookEvent.EventId,
                webhookEvent.EventType,
                webhookEvent.SessionId,
                webhookEvent.PaymentIntentId);

            var result = await Sender.Send(command, cancellationToken);
            return HandleResult(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
