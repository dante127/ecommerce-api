using Asp.Versioning;
using ECommerce.Application.Features.Cart.Commands;
using ECommerce.Application.Features.Cart.DTOs;
using ECommerce.Application.Features.Cart.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Api.Controllers.v1;

[ApiVersion("1.0")]
[Authorize]
public sealed class CartController : BaseApiController
{
    public CartController(ISender sender) : base(sender)
    {
    }

    [HttpGet]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCart(CancellationToken cancellationToken)
    {
        var query = new GetCartQuery();
        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("items")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItem([FromBody] AddItemToCartRequest request, CancellationToken cancellationToken)
    {
        var command = new AddItemToCartCommand(request.ProductId, request.Quantity);
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("items/{productId:guid}")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateItem(Guid productId, [FromBody] UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateCartItemCommand(productId, request.Quantity);
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("items/{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveItem(Guid productId, CancellationToken cancellationToken)
    {
        var command = new RemoveCartItemCommand(productId);
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearCart(CancellationToken cancellationToken)
    {
        var command = new ClearCartCommand();
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }
}
