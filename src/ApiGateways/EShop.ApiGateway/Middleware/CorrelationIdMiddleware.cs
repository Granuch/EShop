using EShop.ApiGateway.Configuration;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string CorrelationItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly GatewayOptions _options;

    public CorrelationIdMiddleware(RequestDelegate next, IOptions<GatewayOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headerName = string.IsNullOrWhiteSpace(_options.CorrelationHeaderName)
            ? "X-Correlation-ID"
            : _options.CorrelationHeaderName;

        var correlationId = context.Request.Headers.TryGetValue(headerName, out var header)
            && !string.IsNullOrWhiteSpace(header)
            ? header.ToString()
            : context.TraceIdentifier;

        context.Items[CorrelationItemKey] = correlationId;
        context.Response.Headers[headerName] = correlationId;

        await _next(context);
    }
}
