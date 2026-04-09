using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Middleware;

[TestFixture]
public class CatalogProxyGuardMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_Returns413_WhenCatalogPayloadExceedsLimit()
    {
        var wasCalled = false;
        var middleware = CreateMiddleware(
            next: _ =>
            {
                wasCalled = true;
                return Task.CompletedTask;
            },
            maxBodySizeBytes: 10);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/categories";
        context.Request.ContentLength = 11;

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(wasCalled, Is.False);
    }

    [Test]
    public async Task InvokeAsync_Maps502To503_WithRetryAfter_ForCatalogPath()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            },
            retryAfterSeconds: 8);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/products";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(context.Response.Headers["Retry-After"].ToString(), Is.EqualTo("8"));
    }

    [Test]
    public async Task InvokeAsync_DoesNotModifyNonCatalogPath()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
    }

    private static CatalogProxyGuardMiddleware CreateMiddleware(
        RequestDelegate next,
        long maxBodySizeBytes = 1024,
        int retryAfterSeconds = 5)
    {
        var options = Options.Create(new CatalogProxyOptions
        {
            MaxRequestBodySizeBytes = maxBodySizeBytes,
            UpstreamUnavailableRetryAfterSeconds = retryAfterSeconds
        });

        return new CatalogProxyGuardMiddleware(next, options);
    }
}
