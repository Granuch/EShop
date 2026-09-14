using EShop.BuildingBlocks.Application;
using Microsoft.AspNetCore.Http;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// The Result-failure counterpart to <see cref="ProblemDetailsExceptionMiddleware"/>, for
/// minimal-API endpoints. Produces the identical envelope, so a given endpoint answers the same
/// shape whether it failed via a Result or a thrown exception.
///
/// <para>The returned IResult builds its ProblemDetails at execute time rather than at call time.
/// That is what lets these replace bare <c>Results.Problem(...)</c> calls inside endpoint lambdas
/// without threading an HttpContext parameter through 39 call sites — the framework hands us the
/// context when it executes the result. It also means traceId is attached here rather than by
/// AddProblemDetails's CustomizeProblemDetails hook, which ProblemHttpResult never consults.</para>
/// </summary>
public static class ProblemResults
{
    /// <summary>
    /// Maps a failed <see cref="Result"/>'s error. The caller supplies the status code — it is
    /// never inferred from the error code here, because the right status for a given code is a
    /// per-service decision (Identity's Auth.InvalidCredentials is a 401, not a 400).
    /// </summary>
    public static IResult For(Error error, int statusCode)
        => new LazyProblemResult(error.Code, error.Message, statusCode);

    public static IResult For(string errorCode, string detail, int statusCode)
        => new LazyProblemResult(errorCode, detail, statusCode);

    private sealed class LazyProblemResult(string errorCode, string detail, int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
            => EShopProblem.WriteAsync(
                httpContext,
                EShopProblem.Create(httpContext, statusCode, detail, errorCode));
    }
}
