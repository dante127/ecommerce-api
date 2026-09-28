using ECommerce.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    private ISender? _sender;
    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected IActionResult HandleResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapErrorToProblem(result.Error);
    }

    protected IActionResult HandleResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return MapErrorToProblem(result.Error);
    }

    private IActionResult MapErrorToProblem(Error error)
    {
        // The status comes from the error own classification rather than from substring
        // matching its code, which is what previously turned any code containing "Token" into a 401.
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(
            statusCode: statusCode,
            title: error.Code,
            detail: error.Message,
            type: $"https://httpstatuses.com/{statusCode}");
    }
}
