using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using EShop.BuildingBlocks.Application.Exceptions;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Identity.API.Infrastructure.Middleware;

/// <summary>
/// Global exception handling middleware that converts exceptions to appropriate HTTP responses
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
        var (statusCode, response) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    Type = "ValidationError",
                    Title = "Validation Failed",
                    Status = (int)HttpStatusCode.BadRequest,
                    Errors = validationEx.Errors,
                    TraceId = context.TraceIdentifier
                }),

            NotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                new ErrorResponse
                {
                    Type = "NotFound",
                    Title = notFoundEx.Message,
                    Status = (int)HttpStatusCode.NotFound,
                    TraceId = context.TraceIdentifier
                }),

            DomainException domainEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    Type = "DomainError",
                    Title = "Business Rule Violation",
                    Detail = domainEx.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    TraceId = context.TraceIdentifier
                }),

            UnauthorizedAccessException => (
                HttpStatusCode.Unauthorized,
                new ErrorResponse
                {
                    Type = "Unauthorized",
                    Title = "Authentication Required",
                    Status = (int)HttpStatusCode.Unauthorized,
                    TraceId = context.TraceIdentifier
                }),

            _ => (
                HttpStatusCode.InternalServerError,
                new ErrorResponse
                {
                    Type = "InternalServerError",
                    Title = "An unexpected error occurred",
                    Status = (int)HttpStatusCode.InternalServerError,
                    TraceId = context.TraceIdentifier
                })
        };

        // Log the exception with appropriate level
        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId}", context.TraceIdentifier);
        }
        else if (exception is ValidationException)
        {
            // ValidationException is expected - log as Information, not Warning
            _logger.LogInformation("Validation failed for request. TraceId: {TraceId}, Errors: {@Errors}",
                context.TraceIdentifier, (exception as ValidationException)?.Errors);
        }
        else
        {
            _logger.LogWarning("Handled exception: {ExceptionType} - {Message}. TraceId: {TraceId}",
                exception.GetType().Name, exception.Message, context.TraceIdentifier);
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
    }
}

/// <summary>
/// Error response following RFC 7807 Problem Details format
/// </summary>
public class ErrorResponse
{
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public int Status { get; init; }
    public string? TraceId { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
}

/// <summary>
/// Extension methods for adding the exception handler middleware
/// </summary>
public static class GlobalExceptionHandlerExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
    }
}
