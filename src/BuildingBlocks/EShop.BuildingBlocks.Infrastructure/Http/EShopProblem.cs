using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Builds the one error envelope every EShop service emits: RFC 7807 ProblemDetails with two
/// extension members, <c>errorCode</c> (the machine discriminator) and <c>traceId</c>.
///
/// <para><c>Type</c> and <c>Title</c> are deliberately left null here. They are filled by
/// <see cref="Results.Problem(ProblemDetails)"/>, whose ProblemHttpResult constructor applies
/// the framework's own ProblemDetailsDefaults for the status code. Hardcoding our own table of
/// RFC 9110 URIs would be a second source of truth that could silently drift from the runtime
/// on a .NET upgrade — and drift would split the contract back in two, because the minimal-API
/// path fills those fields from the framework table no matter what we do.</para>
/// </summary>
public static class EShopProblem
{
    /// <summary>
    /// Extension keys are written verbatim by <c>[JsonExtensionData]</c> — the serializer's
    /// PropertyNamingPolicy does not apply to them — so these must already be camelCase.
    /// </summary>
    public const string ErrorCodeKey = "errorCode";

    public const string TraceIdKey = "traceId";
    public const string ErrorsKey = "errors";

    public static ProblemDetails Create(
        HttpContext httpContext,
        int status,
        string? detail = null,
        string? errorCode = null,
        IDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Detail = detail
        };

        if (!string.IsNullOrEmpty(errorCode))
        {
            problem.Extensions[ErrorCodeKey] = errorCode;
        }

        problem.Extensions[TraceIdKey] = httpContext.TraceIdentifier;

        // Deliberately an extension member on a plain ProblemDetails rather than a
        // ValidationProblemDetails: serializing the latter through a ProblemDetails-typed
        // reference silently drops its Errors property.
        if (errors is { Count: > 0 })
        {
            problem.Extensions[ErrorsKey] = errors;
        }

        return problem;
    }

    /// <summary>
    /// Writes the response through the same code path the minimal-API endpoints use, so the
    /// exception path and the Result path are byte-identical for a given status.
    /// </summary>
    public static Task WriteAsync(HttpContext httpContext, ProblemDetails problem)
        => Results.Problem(problem).ExecuteAsync(httpContext);
}
