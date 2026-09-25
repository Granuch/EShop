using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Middleware;
using EShop.ApiGateway.Notifications;
using EShop.ApiGateway.Simulation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Middleware;

[TestFixture]
public class EmailTriggerMiddlewareTests
{
    private static readonly string[] Operators = ["ops@eshop.local"];

    private static (EmailTriggerMiddleware Middleware, TestNotificationService Sink) Build(
        GatewayOptions options,
        int statusCode)
    {
        var sink = new TestNotificationService();
        var middleware = new EmailTriggerMiddleware(
            async context =>
            {
                context.Response.StatusCode = statusCode;
                await Task.CompletedTask;
            },
            sink,
            Options.Create(options));
        return (middleware, sink);
    }

    private static DefaultHttpContext Request(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        return context;
    }

    [Test]
    public async Task InvokeAsync_QueuesSimulationFailureEvent_WhenSimulationReturnsServerError()
    {
        var (middleware, sink) = Build(
            new GatewayOptions { EnableSimulationFailureEmailNotifications = true, OperationsEmailRecipients = Operators },
            StatusCodes.Status503ServiceUnavailable);

        var context = new DefaultHttpContext();
        context.Items[SimulationContextKeys.Enabled] = true;
        context.Items[SimulationContextKeys.Profile] = new SimulationProfile("orders", "/api/v1/orders", true, 0, 0, 0, [], null, "default");

        await middleware.InvokeAsync(context);

        Assert.That(sink.Items.Count, Is.EqualTo(1));
        Assert.That(sink.Items[0].EventType, Is.EqualTo("SimulationFailureTriggered"));
    }

    [Test]
    public async Task InvokeAsync_QueuesRateLimitEvent_WhenResponseIs429()
    {
        var (middleware, sink) = Build(
            new GatewayOptions { EnableRateLimitEmailNotifications = true, OperationsEmailRecipients = Operators },
            StatusCodes.Status429TooManyRequests);

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.That(sink.Items.Count, Is.EqualTo(1));
        Assert.That(sink.Items[0].EventType, Is.EqualTo("RateLimitExceeded"));
    }

    [Test]
    public async Task InvokeAsync_QueuesCriticalSuccessEvent_ForAWriteUnderACriticalPrefix()
    {
        var (middleware, sink) = Build(
            new GatewayOptions
            {
                EnableCriticalSuccessEmailNotifications = true,
                CriticalSuccessPathPrefixes = ["/api/v1/payments"],
                OperationsEmailRecipients = Operators
            },
            StatusCodes.Status200OK);

        await middleware.InvokeAsync(Request(HttpMethods.Post, "/api/v1/payments/confirm"));

        Assert.That(sink.Items.Count, Is.EqualTo(1));
        Assert.That(sink.Items[0].EventType, Is.EqualTo("CriticalOperationCompleted"));
    }

    /// <summary>
    /// Frontend-contracts F-55. A read under a critical prefix is not a critical operation. It used to be one, so a
    /// client polling <c>GET /api/v1/payments/{id}</c> queued a notice per poll.
    /// </summary>
    [TestCase("GET")]
    [TestCase("HEAD")]
    [TestCase("OPTIONS")]
    public async Task InvokeAsync_QueuesNothing_ForAReadUnderACriticalPrefix(string method)
    {
        var (middleware, sink) = Build(
            new GatewayOptions
            {
                EnableCriticalSuccessEmailNotifications = true,
                CriticalSuccessPathPrefixes = ["/api/v1/payments"],
                OperationsEmailRecipients = Operators
            },
            StatusCodes.Status200OK);

        await middleware.InvokeAsync(Request(method, "/api/v1/payments/5b0f3c2e-0000-0000-0000-000000000001"));

        Assert.That(sink.Items, Is.Empty);
    }

    /// <summary>A failure is still worth a notice whatever the method: the read filter applies to successes only.</summary>
    [Test]
    public async Task InvokeAsync_QueuesDownstreamFailure_ForAFailedRead()
    {
        var (middleware, sink) = Build(
            new GatewayOptions { EnableProxyFailureEmailNotifications = true, OperationsEmailRecipients = Operators },
            StatusCodes.Status502BadGateway);

        await middleware.InvokeAsync(Request(HttpMethods.Get, "/api/v1/payments/5b0f3c2e-0000-0000-0000-000000000001"));

        Assert.That(sink.Items.Select(i => i.EventType), Is.EqualTo(new[] { "DownstreamFailure" }));
    }

    /// <summary>
    /// Frontend-contracts F-55. Notices go to operators only, so with no operator configured nothing is queued at all —
    /// for any event type.
    /// </summary>
    [TestCase(StatusCodes.Status429TooManyRequests, "GET")]
    [TestCase(StatusCodes.Status500InternalServerError, "GET")]
    [TestCase(StatusCodes.Status200OK, "POST")]
    public async Task InvokeAsync_QueuesNothing_WhenNoOperatorIsConfigured(int statusCode, string method)
    {
        var (middleware, sink) = Build(
            new GatewayOptions
            {
                EnableRateLimitEmailNotifications = true,
                EnableProxyFailureEmailNotifications = true,
                EnableCriticalSuccessEmailNotifications = true,
                CriticalSuccessPathPrefixes = ["/api/v1/payments"],
                OperationsEmailRecipients = ["", "   "]
            },
            statusCode);

        await middleware.InvokeAsync(Request(method, "/api/v1/payments/create-intent"));

        Assert.That(sink.Items, Is.Empty);
    }

    private sealed class TestNotificationService : IEmailNotificationService
    {
        public List<EmailNotificationContext> Items { get; } = [];

        public Task QueueAsync(EmailNotificationContext context, CancellationToken cancellationToken = default)
        {
            Items.Add(context);
            return Task.CompletedTask;
        }
    }
}
