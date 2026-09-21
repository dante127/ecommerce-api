using ECommerce.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Api.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

        var (statusCode, title, detail, extensions) = exception switch
        {
            ValidationException validationEx => (
                StatusCodes.Status400BadRequest,
                "ValidationFailure",
                "One or more validation errors occurred.",
                validationEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => (object)g.Select(e => e.ErrorMessage).ToArray())),

            InvalidStateTransitionException stateEx => (
                StatusCodes.Status400BadRequest,
                "InvalidStateTransition",
                stateEx.Message,
                null),

            InsufficientStockException stockEx => (
                StatusCodes.Status409Conflict,
                "InsufficientStock",
                stockEx.Message,
                null),

            DomainException domainEx => (
                StatusCodes.Status400BadRequest,
                "DomainError",
                domainEx.Message,
                null),

            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "ConcurrencyConflict",
                "The resource was modified by another request. Please refresh and try again.",
                null),

            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "You are not authorized to perform this operation.",
                null),

            _ => (
                StatusCodes.Status500InternalServerError,
                "InternalServerError",
                "An unexpected error occurred. Please contact support.",
                null)
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.com/{statusCode}"
        };

        var traceId = httpContext.TraceIdentifier;
        if (!string.IsNullOrEmpty(traceId))
        {
            problemDetails.Extensions["traceId"] = traceId;
        }

        if (extensions != null)
        {
            foreach (var (key, value) in extensions)
            {
                problemDetails.Extensions[key] = value;
            }
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
