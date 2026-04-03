using System.Net;
using System.Text.Json;
using EShop.BuildingBlocks.Application.Exceptions;
using EShop.BuildingBlocks.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EShop.Ordering.API.Infrastructure.Middleware;

/// <summary>
/// Global exception handling middleware following RFC 7807 Problem Details format.
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

            NotFoundException => (
                HttpStatusCode.NotFound,
                new ErrorResponse
                {
                    Type = "NotFound",
                    Title = "Requested resource was not found.",
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

            DomainException => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    Type = "DomainError",
                    Title = "Business rule validation failed.",
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
                    Type = "InternalError",
                    Title = "An unexpected error occurred",
                    Status = (int)HttpStatusCode.InternalServerError,
                    TraceId = context.TraceIdentifier
                })
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception: {Type} - {Message}", exception.GetType().Name, exception.Message);
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }
}

public class ErrorResponse
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public int Status { get; set; }
    public string? TraceId { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}

public static class GlobalExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
    }
}
