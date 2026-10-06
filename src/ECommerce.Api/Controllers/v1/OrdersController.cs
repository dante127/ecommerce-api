using Asp.Versioning;
using ECommerce.Application.Common.Authorization;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Orders.Commands;
using ECommerce.Application.Features.Orders.DTOs;
using ECommerce.Application.Features.Orders.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Api.Controllers.v1;

[ApiVersion("1.0")]
[Authorize]
public sealed class OrdersController : BaseApiController
{
    public OrdersController(ISender sender) : base(sender)
    {
    }

    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request, CancellationToken cancellationToken)
    {
        var command = new CheckoutCommand(request.ShippingAddress);
        var result = await Sender.Send(command, cancellationToken);

        if (result.IsSuccess)
        {
            return CreatedAtAction(nameof(GetOrderById), new { id = result.Value.Id }, result.Value);
        }

        return HandleResult(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedList<OrderSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new GetOrdersQuery(page, pageSize);
        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetOrderByIdQuery(id);
        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelOrder(Guid id, [FromBody] CancelOrderRequest? request, CancellationToken cancellationToken)
    {
        var command = new CancelOrderCommand(id, request?.Reason);
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TransitionStatus(Guid id, [FromBody] TransitionOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var command = new TransitionOrderStatusCommand(id, request.Status);
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }
}
