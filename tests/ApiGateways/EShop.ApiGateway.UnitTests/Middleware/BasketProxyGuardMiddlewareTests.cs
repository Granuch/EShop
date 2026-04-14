using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Middleware;

[TestFixture]
public class BasketProxyGuardMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_Returns413_WhenBasketPayloadExceedsLimit()
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
        context.Request.Path = "/api/v1/basket/user-1/items";
        context.Request.ContentLength = 11;

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(called, Is.False);
    }

    [Test]
    public async Task InvokeAsync_Maps502To503_WithRetryAfter_ForBasketPath()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            },
            retryAfterSeconds: 7);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/basket/user-1";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(context.Response.Headers["Retry-After"].ToString(), Is.EqualTo("7"));
    }

    [Test]
    public async Task InvokeAsync_DoesNotModifyNonBasketPath()
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

    [Test]
    public async Task InvokeAsync_DoesNotTreatBasketballPathAsBasketRoute()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/basketball";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
    }

    private static BasketProxyGuardMiddleware CreateMiddleware(
        RequestDelegate next,
        long maxBodySizeBytes = 1024,
        int retryAfterSeconds = 5)
    {
        var options = Options.Create(new BasketProxyOptions
        {
            MaxRequestBodySizeBytes = maxBodySizeBytes,
            UpstreamUnavailableRetryAfterSeconds = retryAfterSeconds
        });

        return new BasketProxyGuardMiddleware(next, options);
    }
}
