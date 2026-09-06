using System.Net;
using System.Text.Json;
using EShop.BuildingBlocks.Application.Exceptions;
using EShop.BuildingBlocks.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EShop.Catalog.API.Infrastructure.Middleware;

/// <summary>
/// Global exception handling middleware that converts exceptions to appropriate HTTP responses.
/// Follows RFC 7807 Problem Details format — aligned with Identity Service.
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

    /// <summary>
    /// Describes a body-binding failure without echoing System.Text.Json's own message, which
    /// embeds the target .NET type ("...could not be mapped to any .NET member contained in type
    /// 'EShop.Catalog.Application.Products.Commands.CreateProduct.CreateProductCommand'") and so
    /// leaks internal namespace and class structure to the caller. The JSON path carries the one
    /// thing the client actually needs — which property was wrong — and nothing else.
    /// </summary>
    private static string DescribeBadRequest(BadHttpRequestException exception)
    {
        if (exception.InnerException is JsonException { Path.Length: > 0 } jsonEx)
            return $"The request body contains an unknown or invalid property: '{jsonEx.Path}'.";

        return "The request body is not valid JSON, or contains a property that does not exist on this endpoint.";
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
                    Title = "Requested resource was not found.",
                    Detail = notFoundEx.Message,
                    Status = (int)HttpStatusCode.NotFound,
                    TraceId = context.TraceIdentifier
                }),

            DbUpdateConcurrencyException => (
                HttpStatusCode.Conflict,
                new ErrorResponse
                {
                    Type = "ConcurrencyConflict",
                    Title = "The resource was modified by another request. Please retry.",
                    Status = (int)HttpStatusCode.Conflict,
                    TraceId = context.TraceIdentifier
                }),

            DbUpdateException dbEx when dbEx.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true
                                     || dbEx.InnerException?.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) == true => (
                HttpStatusCode.Conflict,
                new ErrorResponse
                {
                    Type = "DuplicateResource",
                    Title = "A resource with the same unique value already exists.",
                    Status = (int)HttpStatusCode.Conflict,
                    TraceId = context.TraceIdentifier
                }),

            // Detail carries the domain's own message ("A product cannot have more than 10
            // images.", "Attribute 'Color' already exists for this product.", ...). Without it
            // every domain rejection is byte-identical over the wire and the only way to tell
            // which rule fired is correlating TraceId against the server log. These strings are
            // authored in our domain layer, so they are safe to return — unlike the DbUpdate*
            // branches above, whose inner messages would leak schema and constraint names.
            DomainException domainEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    Type = "DomainError",
                    Title = "Business rule validation failed.",
                    Detail = domainEx.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    TraceId = context.TraceIdentifier
                }),

            // Minimal APIs wrap a body-binding failure (malformed JSON, and — since
            // UnmappedMemberHandling.Disallow — any unknown field) in BadHttpRequestException.
            // Without this branch it falls through to the catch-all and a client's typo'd
            // property name is reported as a 500.
            BadHttpRequestException badRequestEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    Type = "MalformedRequest",
                    Title = "The request body could not be read.",
                    Detail = DescribeBadRequest(badRequestEx),
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

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId}", context.TraceIdentifier);
        }
        else if (exception is ValidationException)
        {
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
