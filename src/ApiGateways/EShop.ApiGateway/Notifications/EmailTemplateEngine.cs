namespace EShop.ApiGateway.Notifications;

public sealed class EmailTemplateEngine : IEmailTemplateEngine
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<EmailTemplateEngine> _logger;

    private static readonly IReadOnlyDictionary<string, string> TemplateFileByEvent =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SimulationResponse"] = "simulation-response.html",
            ["SimulationFailureTriggered"] = "simulation-failure.html",
            ["RateLimitExceeded"] = "rate-limit.html",
            ["DownstreamFailure"] = "downstream-failure.html",
            ["CriticalOperationCompleted"] = "critical-success.html"
        };

    public EmailTemplateEngine(
        IWebHostEnvironment environment,
        ILogger<EmailTemplateEngine> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public (string Subject, string HtmlBody) Render(EmailNotificationContext context)
    {
        var subject = $"[Gateway] {context.EventType} on {context.Route}";
        var body = RenderBody(context);

        return (subject, body);
    }

    private string RenderBody(EmailNotificationContext context)
    {
        var templatePath = ResolveTemplatePath(context.EventType);
        if (templatePath is null || !File.Exists(templatePath))
        {
            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                _logger.LogWarning("Gateway email template file not found. EventType={EventType}, Path={TemplatePath}", context.EventType, templatePath);
            }

            return RenderFallback(context);
        }

        var template = File.ReadAllText(templatePath);

        return template
            .Replace("{{EventType}}", context.EventType, StringComparison.Ordinal)
            .Replace("{{Route}}", context.Route, StringComparison.Ordinal)
            .Replace("{{Path}}", context.Path, StringComparison.Ordinal)
            .Replace("{{StatusCode}}", context.StatusCode?.ToString() ?? "n/a", StringComparison.Ordinal)
            .Replace("{{UserId}}", context.UserId ?? "anonymous", StringComparison.Ordinal)
            .Replace("{{UserEmail}}", context.UserEmail ?? "n/a", StringComparison.Ordinal)
            .Replace("{{CorrelationId}}", context.CorrelationId, StringComparison.Ordinal)
            .Replace("{{OccurredAtUtc}}", context.OccurredAtUtc.ToString("O"), StringComparison.Ordinal);
    }

    private string? ResolveTemplatePath(string eventType)
    {
        if (!TemplateFileByEvent.TryGetValue(eventType, out var fileName))
        {
            fileName = "simulation-response.html";
        }

        return Path.Combine(_environment.ContentRootPath, "Templates", fileName);
    }

    private static string RenderFallback(EmailNotificationContext context)
    {
        return $"""
            <h3>Gateway event</h3>
            <ul>
                <li>Type: {context.EventType}</li>
                <li>Route: {context.Route}</li>
                <li>Path: {context.Path}</li>
                <li>Status: {context.StatusCode}</li>
                <li>UserId: {context.UserId ?? "anonymous"}</li>
                <li>CorrelationId: {context.CorrelationId}</li>
                <li>OccurredAtUtc: {context.OccurredAtUtc:O}</li>
            </ul>
            """;
    }
}
