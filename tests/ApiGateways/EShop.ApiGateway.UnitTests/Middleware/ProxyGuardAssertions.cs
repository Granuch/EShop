using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.ApiGateway.UnitTests.Middleware;

/// <summary>
/// DEBT-02. Shared setup for the four <c>*ProxyGuardMiddleware</c> fixtures, which now have to
/// assert on response <i>bodies</i> and not just status codes.
///
/// <para>
/// The guards previously wrote an ad-hoc <c>{ error, message }</c> object, so the gateway answered
/// a payload-too-large rejection in one shape and an unhandled exception in another. They now go
/// through <see cref="EShop.BuildingBlocks.Infrastructure.Http.EShopProblem"/> like everything
/// else. Nothing caught the old divergence because these fixtures only ever asserted the status
/// code — which is exactly why the body assertions below exist.
/// </para>
/// </summary>
internal static class ProxyGuardAssertions
{
    /// <summary>
    /// A context that can actually be written to. <see cref="DefaultHttpContext"/> defaults
    /// <c>Response.Body</c> to <see cref="Stream.Null"/>, so a middleware writing a body appears to
    /// succeed and the test reads back an empty string with no error. It also needs
    /// <c>RequestServices</c>, because writing through <c>Results.Problem</c> resolves options and
    /// logging from the container.
    /// </summary>
    public static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .AddSingleton(TimeProvider.System)
                .BuildServiceProvider()
        };

        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        return context;
    }

    /// <summary>
    /// Asserts the canonical envelope: problem+json content type, the expected
    /// <c>errorCode</c> discriminator, a <c>traceId</c>, and a <c>status</c> matching the response.
    /// </summary>
    public static async Task AssertProblemBodyAsync(HttpContext context, string expectedErrorCode)
    {
        Assert.That(context.Response.ContentType, Does.StartWith("application/problem+json"),
            "the gateway must not answer in a second error shape");

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        Assert.That(root.TryGetProperty("errorCode", out var errorCode), Is.True,
            "errorCode is the machine discriminator and is written verbatim, so it must be camelCase");
        Assert.That(errorCode.GetString(), Is.EqualTo(expectedErrorCode));

        Assert.That(root.TryGetProperty("traceId", out var traceId), Is.True,
            "traceId is what correlates the response with the server log");
        Assert.That(traceId.GetString(), Is.Not.Null.And.Not.Empty);

        Assert.That(root.GetProperty("status").GetInt32(), Is.EqualTo(context.Response.StatusCode));

        Assert.That(root.TryGetProperty("error", out _), Is.False,
            "the old ad-hoc shape must be gone, not emitted alongside the new one");
    }
}
