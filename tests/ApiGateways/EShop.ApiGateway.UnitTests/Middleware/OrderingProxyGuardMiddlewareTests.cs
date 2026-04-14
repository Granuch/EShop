using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Middleware;

[TestFixture]
public class OrderingProxyGuardMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_Returns413_WhenOrderingPayloadExceedsLimit()
    {
        var called = false;
        var middleware = CreateMiddleware(
            next: _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            maxBodySizeBytes: 10);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";
        context.Request.ContentLength = 11;

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(called, Is.False);
    }

    [Test]
    public async Task InvokeAsync_Maps502To503_WithRetryAfter_ForOrderingPath()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            },
            retryAfterSeconds: 9);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(context.Response.Headers["Retry-After"].ToString(), Is.EqualTo("9"));
    }

    [Test]
    public async Task InvokeAsync_Maps502To503_ForUserOrdersPath()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            },
            retryAfterSeconds: 6);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/users/abc/orders";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(context.Response.Headers["Retry-After"].ToString(), Is.EqualTo("6"));
    }

    [Test]
    public async Task InvokeAsync_DoesNotModifyNonOrderingPath()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/products";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
    }

    [Test]
    public async Task InvokeAsync_DoesNotTreatOrdersArchivePathAsOrderingRoute()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders-archive";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
    }

    private static OrderingProxyGuardMiddleware CreateMiddleware(
        RequestDelegate next,
        long maxBodySizeBytes = 1024,
        int retryAfterSeconds = 5)
    {
        var options = Options.Create(new OrderingProxyOptions
        {
            MaxRequestBodySizeBytes = maxBodySizeBytes,
            UpstreamUnavailableRetryAfterSeconds = retryAfterSeconds
        });

        return new OrderingProxyGuardMiddleware(next, options);
    }
}
