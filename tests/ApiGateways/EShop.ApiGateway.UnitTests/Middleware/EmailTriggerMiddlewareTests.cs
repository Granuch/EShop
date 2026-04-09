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
    [Test]
    public async Task InvokeAsync_QueuesSimulationFailureEvent_WhenSimulationReturnsServerError()
    {
        var sink = new TestNotificationService();
        var options = Options.Create(new GatewayOptions
        {
            EnableSimulationFailureEmailNotifications = true
        });

        var middleware = new EmailTriggerMiddleware(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await Task.CompletedTask;
            },
            sink,
            options);

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
        var sink = new TestNotificationService();
        var options = Options.Create(new GatewayOptions
        {
            EnableRateLimitEmailNotifications = true
        });

        var middleware = new EmailTriggerMiddleware(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await Task.CompletedTask;
            },
            sink,
            options);

        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.That(sink.Items.Count, Is.EqualTo(1));
        Assert.That(sink.Items[0].EventType, Is.EqualTo("RateLimitExceeded"));
    }

    [Test]
    public async Task InvokeAsync_QueuesCriticalSuccessEvent_WhenPathMatchesCriticalPrefix()
    {
        var sink = new TestNotificationService();
        var options = Options.Create(new GatewayOptions
        {
            EnableCriticalSuccessEmailNotifications = true,
            CriticalSuccessPathPrefixes = ["/api/v1/payments"]
        });

        var middleware = new EmailTriggerMiddleware(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await Task.CompletedTask;
            },
            sink,
            options);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/payments/confirm";

        await middleware.InvokeAsync(context);

        Assert.That(sink.Items.Count, Is.EqualTo(1));
        Assert.That(sink.Items[0].EventType, Is.EqualTo("CriticalOperationCompleted"));
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
