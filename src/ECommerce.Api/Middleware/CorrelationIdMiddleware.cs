using Serilog.Context;

namespace ECommerce.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    private const string CorrelationIdHeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationIdHeaderName))
            {
                context.Response.Headers.Append(CorrelationIdHeaderName, correlationId);
            }
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        // A client-supplied id is echoed into response headers and every log line on the request,
        // so it must be bounded and printable before it is trusted anywhere.
        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var headerValue) &&
            IsValidCorrelationId(headerValue.ToString()))
        {
            return headerValue.ToString();
        }

        return Guid.NewGuid().ToString("D");
    }

    private static bool IsValidCorrelationId(string value) =>
        value.Length <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
