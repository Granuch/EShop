using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using static EShop.ApiGateway.UnitTests.Middleware.ProxyGuardAssertions;

namespace EShop.ApiGateway.UnitTests.Middleware;

/// <summary>G6 (Admin panel S12). The fifth proxy guard, held to the same contract as the other four.</summary>
[TestFixture]
public class NotificationProxyGuardMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_Returns413_WhenNotificationPayloadExceedsLimit()
    {
        var called = false;
        var middleware = CreateMiddleware(
            next: _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            maxBodySizeBytes: 10);

        var context = CreateContext("/api/v1/notifications/00000000-0000-0000-0000-000000000001/resend");
        context.Request.ContentLength = 11;

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(called, Is.False);
        await AssertProblemBodyAsync(context, "Request.PayloadTooLarge");
    }

    [Test]
    public async Task InvokeAsync_Maps502To503_WithRetryAfter_ForNotificationPath()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            },
            retryAfterSeconds: 7);

        var context = CreateContext("/api/v1/notifications");

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(context.Response.Headers["Retry-After"].ToString(), Is.EqualTo("7"),
            "Retry-After must survive the problem body being written after it");
        await AssertProblemBodyAsync(context, "Gateway.UpstreamUnavailable");
    }

    [Test]
    public async Task InvokeAsync_DoesNotModifyNonNotificationPath()
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
    public async Task InvokeAsync_DoesNotTreatALongerSegmentAsTheNotificationRoute()
    {
        // StartsWithSegments, not StartsWith: "/api/v1/notificationsettings" is a different resource and must not
        // inherit this guard's body cap or its 503 rewrite.
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/notificationsettings";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
    }

    private static NotificationProxyGuardMiddleware CreateMiddleware(
        RequestDelegate next,
        long maxBodySizeBytes = 1024,
        int retryAfterSeconds = 5)
    {
        var options = Options.Create(new NotificationProxyOptions
        {
            MaxRequestBodySizeBytes = maxBodySizeBytes,
            UpstreamUnavailableRetryAfterSeconds = retryAfterSeconds
        });

        return new NotificationProxyGuardMiddleware(next, options);
    }
}
