using System.Security.Claims;
using System.Diagnostics;
using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Notifications;
using EShop.ApiGateway.Simulation;
using EShop.ApiGateway.Telemetry;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Middleware;

public sealed class EmailTriggerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IEmailNotificationService _emailNotificationService;
    private readonly GatewayOptions _options;

    public EmailTriggerMiddleware(
        RequestDelegate next,
        IEmailNotificationService emailNotificationService,
        IOptions<GatewayOptions> options)
    {
        _next = next;
        _emailNotificationService = emailNotificationService;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();

        await _next(context);

        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var route = ResolveRouteName(context);
        var isSimulation = context.Items.TryGetValue(SimulationContextKeys.Enabled, out var simulationObj)
            && simulationObj is bool simulationEnabled
            && simulationEnabled;

        GatewayTelemetry.RecordRequest(
            mode: GatewayRequestClassifier.GetRequestMode(isSimulation),
            route: route,
            statusCode: context.Response.StatusCode,
            elapsedMs: elapsedMs);

        if (!TryBuildNotification(context, isSimulation, out var notification))
        {
            return;
        }

        if (notification.EventType.Equals("SimulationFailureTriggered", StringComparison.Ordinal)
            || GatewayRequestClassifier.IsServerFailureStatus(notification.StatusCode ?? StatusCodes.Status200OK)
               && isSimulation)
        {
            GatewayTelemetry.RecordSimulatedFailure(notification.Route, notification.StatusCode ?? StatusCodes.Status500InternalServerError);
        }

        if (notification.EventType.Equals("RateLimitExceeded", StringComparison.Ordinal)
            || GatewayRequestClassifier.IsRateLimitStatus(notification.StatusCode ?? StatusCodes.Status200OK))
        {
            GatewayTelemetry.RecordRateLimited(notification.Route);
        }

        GatewayTelemetry.RecordEmailQueued(notification.EventType);

        await _emailNotificationService.QueueAsync(notification, context.RequestAborted);
    }

    private bool TryBuildNotification(HttpContext context, bool isSimulation, out EmailNotificationContext notification)
    {
        var statusCode = context.Response.StatusCode;

        var routeName = ResolveRouteName(context);
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? context.User.Identity?.Name;
        var userEmail = context.User.FindFirstValue(ClaimTypes.Email)
            ?? context.User.FindFirstValue("email");

        string? eventType = null;

        if (isSimulation)
        {
            if (_options.EnableSimulationFailureEmailNotifications && statusCode >= 500)
            {
                eventType = "SimulationFailureTriggered";
            }
            else if (_options.EnableAuditEmailNotifications)
            {
                eventType = "SimulationResponse";
            }
        }
        else
        {
            if (_options.EnableRateLimitEmailNotifications && statusCode == StatusCodes.Status429TooManyRequests)
            {
                eventType = "RateLimitExceeded";
            }
            else if (_options.EnableProxyFailureEmailNotifications && statusCode >= 500)
            {
                eventType = "DownstreamFailure";
            }
            else if (_options.EnableCriticalSuccessEmailNotifications
                && statusCode is >= 200 and < 300
                && IsCriticalRoute(context.Request.Path, _options.CriticalSuccessPathPrefixes))
            {
                eventType = "CriticalOperationCompleted";
            }
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            notification = default!;
            return false;
        }

        notification = new EmailNotificationContext(
            EventType: eventType,
            Route: routeName,
            StatusCode: statusCode,
            Path: context.Request.Path,
            UserId: userId,
            UserEmail: userEmail,
            CorrelationId: context.Items.TryGetValue(CorrelationIdMiddleware.CorrelationItemKey, out var correlationObj)
                ? correlationObj?.ToString() ?? context.TraceIdentifier
                : context.TraceIdentifier,
            OccurredAtUtc: DateTime.UtcNow);

        return true;
    }

    private static bool IsCriticalRoute(PathString requestPath, IReadOnlyList<string> prefixes)
    {
        if (prefixes.Count == 0)
        {
            return false;
        }

        var path = requestPath.Value;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix)
                && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveRouteName(HttpContext context)
    {
        if (context.Items.TryGetValue(SimulationContextKeys.Profile, out var profileObj)
            && profileObj is SimulationProfile profile)
        {
            return profile.RouteId;
        }

        return context.GetEndpoint()?.DisplayName ?? "proxy-route";
    }
}
