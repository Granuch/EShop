using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Telemetry;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Notifications;

/// <summary>
/// Sends the gateway's operational notices to the operators in <see cref="GatewayOptions.OperationsEmailRecipients"/>.
/// <para>Frontend-contracts F-55: this used to send each notice to the user whose request caused it, resolving their
/// address through Identity's internal contact endpoint. A customer paying for an order was emailed a
/// "CriticalOperationCompleted" notice, with the route id and correlation id, for every payment call, and a
/// "DownstreamFailure" notice whenever a service failed them. The caller's id stays in the body, for the operator.</para>
/// </summary>
public sealed class GatewayEmailDispatcher : BackgroundService
{
    private const int MaxSendAttempts = 3;

    private readonly GatewayEmailQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly GatewayOptions _options;
    private readonly ILogger<GatewayEmailDispatcher> _logger;

    public GatewayEmailDispatcher(
        GatewayEmailQueue queue,
        IServiceProvider serviceProvider,
        IOptions<GatewayOptions> options,
        ILogger<GatewayEmailDispatcher> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _options = options.Value;
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

                // Never the caller: context.UserEmail and context.UserId describe who caused the notice, not who reads it.
                var recipients = _options.EffectiveOperationsEmailRecipients;
                if (recipients.Count == 0)
                {
                    _logger.LogDebug("Skipping gateway notice {EventType}: no operations recipient is configured. CorrelationId={CorrelationId}", context.EventType, context.CorrelationId);
                    GatewayTelemetry.RecordEmailSent(context.EventType, "skipped-no-recipient");
                    continue;
                }

                using var scope = _serviceProvider.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                var template = scope.ServiceProvider.GetRequiredService<IEmailTemplateEngine>();

                var rendered = template.Render(context);
                foreach (var to in recipients)
                {
                    await SendWithRetryAsync(sender, to, rendered.Subject, rendered.HtmlBody, context, stoppingToken);
                }

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
