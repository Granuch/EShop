using System.Text.Json;
using EShop.BuildingBlocks.Application.Exceptions;
using EShop.BuildingBlocks.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidationException = EShop.BuildingBlocks.Application.Exceptions.ValidationException;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Maps an exception to a problem response, or returns null to let the next mapper try.
/// </summary>
public delegate ProblemDetails? ExceptionProblemMapper(Exception exception, HttpContext context);

/// <summary>
/// The per-service branch set. Services compose only the branches they handled before this was
/// centralised — Basket has never had a NotFoundException branch and must not gain one, Identity
/// has never had the DbUpdate branches — so the shared middleware adds no behaviour by default.
/// <see cref="AddNotFound"/> is opt-in for exactly that reason.
/// </summary>
public sealed class ProblemDetailsExceptionOptions
{
    public IList<ExceptionProblemMapper> Mappers { get; } = [];

    public ProblemDetailsExceptionOptions Add(ExceptionProblemMapper mapper)
    {
        Mappers.Add(mapper);
        return this;
    }

    /// <summary>
    /// The three branches every service already had: FluentValidation failures, domain rule
    /// violations, and unauthenticated access.
    /// </summary>
    public ProblemDetailsExceptionOptions AddCommon()
        => Add((exception, context) => exception switch
        {
            ValidationException validationEx => EShopProblem.Create(
                context,
                StatusCodes.Status400BadRequest,
                detail: "One or more validation errors occurred.",
                errorCode: ProblemErrorCodes.ValidationError,
                errors: validationEx.Errors),

            // Detail carries the domain's own message ("A product cannot have more than 10
            // images.", "Attribute 'Color' already exists for this product.", ...). Without it
            // every domain rejection is byte-identical over the wire and the only way to tell
            // which rule fired is correlating traceId against the server log. These strings are
            // authored in our domain layer, so they are safe to return — unlike the DbUpdate*
            // branches below, whose inner messages would leak schema and constraint names.
            DomainException domainEx => EShopProblem.Create(
                context,
                StatusCodes.Status400BadRequest,
                detail: domainEx.Message,
                errorCode: ProblemErrorCodes.DomainError),

            UnauthorizedAccessException => EShopProblem.Create(
                context,
                StatusCodes.Status401Unauthorized,
                detail: "Authentication is required to access this resource.",
                errorCode: ProblemErrorCodes.Unauthorized),

            _ => null
        });

    public ProblemDetailsExceptionOptions AddNotFound()
        => Add((exception, context) => exception is NotFoundException notFoundEx
            ? EShopProblem.Create(
                context,
                StatusCodes.Status404NotFound,
                detail: notFoundEx.Message,
                errorCode: ProblemErrorCodes.NotFound)
            : null);

    /// <summary>
    /// Must be registered BEFORE <see cref="AddEfDuplicateKey"/> and
    /// <see cref="AddEfPersistenceConflict"/> — DbUpdateConcurrencyException derives from
    /// DbUpdateException, so a broader mapper registered first would swallow it.
    /// </summary>
    public ProblemDetailsExceptionOptions AddEfConcurrency()
        => Add((exception, context) => exception is DbUpdateConcurrencyException
            ? EShopProblem.Create(
                context,
                StatusCodes.Status409Conflict,
                detail: "The resource was modified by another request. Please retry.",
                errorCode: ProblemErrorCodes.ConcurrencyConflict)
            : null);

    /// <summary>
    /// Detail stays generic on purpose: the inner message names the violated constraint and
    /// would leak schema detail to the caller.
    /// </summary>
    public ProblemDetailsExceptionOptions AddEfDuplicateKey()
        => Add((exception, context) => exception is DbUpdateException dbEx
            && (dbEx.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true
                || dbEx.InnerException?.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) == true)
            ? EShopProblem.Create(
                context,
                StatusCodes.Status409Conflict,
                detail: "A resource with the same unique value already exists.",
                errorCode: ProblemErrorCodes.DuplicateResource)
            : null);

    /// <summary>
    /// Payment's broader variant: any persistence failure is a 409, with no duplicate-key sniff.
    /// </summary>
    public ProblemDetailsExceptionOptions AddEfPersistenceConflict()
        => Add((exception, context) => exception is DbUpdateException
            ? EShopProblem.Create(
                context,
                StatusCodes.Status409Conflict,
                detail: "The request conflicts with the current state of the resource.",
                errorCode: ProblemErrorCodes.PersistenceConflict)
            : null);

    /// <summary>
    /// Minimal APIs wrap a body-binding failure (malformed JSON, and — where
    /// UnmappedMemberHandling.Disallow is configured — any unknown field) in
    /// BadHttpRequestException. Without this branch a client's typo'd property name is a 500.
    /// </summary>
    public ProblemDetailsExceptionOptions AddMalformedJsonBody()
        => Add((exception, context) => exception is BadHttpRequestException badRequestEx
            ? EShopProblem.Create(
                context,
                StatusCodes.Status400BadRequest,
                detail: DescribeBadRequest(badRequestEx),
                errorCode: ProblemErrorCodes.MalformedRequest)
            : null);

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
        {
            return $"The request body contains an unknown or invalid property: '{jsonEx.Path}'.";
        }

        return "The request body is not valid JSON, or contains a property that does not exist on this endpoint.";
    }
}
