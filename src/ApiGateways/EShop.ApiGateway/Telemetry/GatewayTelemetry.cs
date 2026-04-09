using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EShop.ApiGateway.Telemetry;

public static class GatewayTelemetry
{
    private static readonly Meter Meter = new("EShop.ApiGateway", "1.0.0");

    private static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        "gateway_requests_total",
        unit: "requests",
        description: "Total gateway requests by mode and route");

    private static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>(
        "gateway_request_duration_ms",
        unit: "ms",
        description: "Gateway request duration in milliseconds");

    private static readonly Counter<long> SimulatedFailuresTotal = Meter.CreateCounter<long>(
        "gateway_simulated_failures_total",
        unit: "failures",
        description: "Total simulated failures by route and status");

    private static readonly Counter<long> EmailQueuedTotal = Meter.CreateCounter<long>(
        "gateway_email_queued_total",
        unit: "notifications",
        description: "Total queued email notifications by event type");

    private static readonly Counter<long> EmailSentTotal = Meter.CreateCounter<long>(
        "gateway_email_sent_total",
        unit: "emails",
        description: "Total sent gateway emails by event type and status");

    private static readonly Counter<long> RateLimitedTotal = Meter.CreateCounter<long>(
        "gateway_rate_limited_total",
        unit: "requests",
        description: "Total rate-limited requests by route");

    public static void RecordRequest(string mode, string route, int statusCode, double elapsedMs)
    {
        var tags = new TagList
        {
            { "mode", mode },
            { "route", route },
            { "status_code", statusCode }
        };

        RequestsTotal.Add(1, tags);
        RequestDurationMs.Record(elapsedMs, tags);
    }

    public static void RecordSimulatedFailure(string route, int statusCode)
    {
        SimulatedFailuresTotal.Add(1, new TagList
        {
            { "route", route },
            { "status_code", statusCode }
        });
    }

    public static void RecordEmailQueued(string eventType)
    {
        EmailQueuedTotal.Add(1, new TagList
        {
            { "event_type", eventType }
        });
    }

    public static void RecordEmailSent(string eventType, string status)
    {
        EmailSentTotal.Add(1, new TagList
        {
            { "event_type", eventType },
            { "status", status }
        });
    }

    public static void RecordRateLimited(string route)
    {
        RateLimitedTotal.Add(1, new TagList
        {
            { "route", route }
        });
    }
}
