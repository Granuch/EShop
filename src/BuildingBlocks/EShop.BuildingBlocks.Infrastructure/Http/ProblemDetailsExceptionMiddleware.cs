using EShop.BuildingBlocks.Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValidationException = EShop.BuildingBlocks.Application.Exceptions.ValidationException;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Converts unhandled exceptions into the canonical RFC 7807 problem response. Replaces the six
/// copy-pasted GlobalExceptionHandlerMiddleware implementations (five services plus the
/// gateway's own variant) that had drifted apart in branch set, error tokens and body shape.
///
/// <para>Which exceptions a service handles is decided entirely by the mappers it registers via
/// <see cref="ProblemDetailsExceptionOptions"/>, so centralising this changed no service's
/// behaviour — only the shape of the body.</para>
/// </summary>
public sealed class ProblemDetailsExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsExceptionMiddleware> _logger;
    private readonly ProblemDetailsExceptionOptions _options;

    public ProblemDetailsExceptionMiddleware(
        RequestDelegate next,
        ILogger<ProblemDetailsExceptionMiddleware> logger,
        IOptions<ProblemDetailsExceptionOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        Microsoft.AspNetCore.Mvc.ProblemDetails? problem = null;

        foreach (var mapper in _options.Mappers)
        {
            problem = mapper(exception, context);
            if (problem is not null)
            {
                break;
            }
        }

        // The catch-all lives here rather than in a mapper so a service cannot forget to
        // register it. Detail is a fixed string: exception.Message on an unrecognised exception
        // is exactly the case where it is most likely to carry internal detail.
        problem ??= EShopProblem.Create(
            context,
            StatusCodes.Status500InternalServerError,
            detail: "An unexpected error occurred.",
            errorCode: ProblemErrorCodes.InternalServerError);

        LogException(context, exception, problem.Status!.Value);

        // Setting a status code after the response has begun throws InvalidOperationException on
        // top of the original exception, losing it. None of the six predecessors checked this.
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Response already started; cannot write problem response. TraceId: {TraceId}",
                context.TraceIdentifier);
            return;
        }

        await EShopProblem.WriteAsync(context, problem);
    }

    private void LogException(HttpContext context, Exception exception, int statusCode)
    {
        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId}", context.TraceIdentifier);
        }
        else if (exception is ValidationException validationEx)
        {
            // Validation failures are expected traffic, not warnings.
            _logger.LogInformation("Validation failed for request. TraceId: {TraceId}, Errors: {@Errors}",
                context.TraceIdentifier, validationEx.Errors);
        }
        else
        {
            _logger.LogWarning("Handled exception: {ExceptionType} - {Message}. TraceId: {TraceId}",
                exception.GetType().Name, exception.Message, context.TraceIdentifier);
        }
    }
}
