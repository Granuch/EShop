using EShop.ApiGateway.Telemetry;

namespace EShop.ApiGateway.Notifications;

public sealed class GatewayEmailDispatcher : BackgroundService
{
    private const int MaxSendAttempts = 3;

    private readonly GatewayEmailQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GatewayEmailDispatcher> _logger;

    public GatewayEmailDispatcher(
        GatewayEmailQueue queue,
        IServiceProvider serviceProvider,
        ILogger<GatewayEmailDispatcher> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var context in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var activity = GatewayActivitySource.Instance.StartActivity("gateway.email.dispatch");
                activity?.SetTag("event_type", context.EventType);
                activity?.SetTag("route", context.Route);
                activity?.SetTag("correlation_id", context.CorrelationId);

                using var scope = _serviceProvider.CreateScope();
                var resolver = scope.ServiceProvider.GetRequiredService<IAccountEmailResolver>();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                var template = scope.ServiceProvider.GetRequiredService<IEmailTemplateEngine>();

                var to = context.UserEmail;

                if (string.IsNullOrWhiteSpace(to))
                {
                    to = await resolver.ResolveByUserIdAsync(context.UserId, stoppingToken);
                }

                if (string.IsNullOrWhiteSpace(to))
                {
                    _logger.LogWarning("Skipping email notification because recipient email cannot be resolved. Route={Route}, CorrelationId={CorrelationId}", context.Route, context.CorrelationId);
                    GatewayTelemetry.RecordEmailSent(context.EventType, "skipped-no-recipient");
                    continue;
                }

                var rendered = template.Render(context);
                await SendWithRetryAsync(sender, to, rendered.Subject, rendered.HtmlBody, context, stoppingToken);
                GatewayTelemetry.RecordEmailSent(context.EventType, "success");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                GatewayTelemetry.RecordEmailSent(context.EventType, "failed");
                _logger.LogError(ex, "Failed to dispatch gateway email notification for CorrelationId={CorrelationId}", context.CorrelationId);
            }
        }
    }

    private async Task SendWithRetryAsync(
        IEmailSender sender,
        string to,
        string subject,
        string htmlBody,
        EmailNotificationContext context,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxSendAttempts; attempt++)
        {
            try
            {
                await sender.SendAsync(to, subject, htmlBody, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxSendAttempts)
            {
                GatewayTelemetry.RecordEmailSent(context.EventType, "retry");
                _logger.LogWarning(
                    ex,
                    "Retrying gateway email send. Attempt={Attempt}, CorrelationId={CorrelationId}",
                    attempt,
                    context.CorrelationId);

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        _logger.LogError(
            "Email send failed after {Attempts} attempts. CorrelationId={CorrelationId}, EventType={EventType}",
            MaxSendAttempts,
            context.CorrelationId,
            context.EventType);

        GatewayTelemetry.RecordEmailSent(context.EventType, "failed-max-retries");
    }
}
