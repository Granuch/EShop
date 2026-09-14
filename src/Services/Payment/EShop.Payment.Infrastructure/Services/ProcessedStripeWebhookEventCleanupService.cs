using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Infrastructure.Services;

/// <summary>
/// Payment audit Stage 10 (D13). Deletes <c>ProcessedStripeWebhookEvents</c> rows older than <see cref="RetentionDays"/>.
/// Nothing deleted them before, unlike <c>outbox_messages</c> and <c>processed_messages</c>, which
/// <c>OutboxCleanupService</c> trims.
/// <para>A row only serves to stop a redelivered event from being applied twice, and Stripe stops redelivering after
/// three days. 30 days leaves room to investigate. An event resent by hand after that is applied again, which is safe:
/// the payment's transitions ignore an event that changes nothing.</para>
/// </summary>
public sealed class ProcessedStripeWebhookEventCleanupService : BackgroundService
{
    public const int RetentionDays = 30;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProcessedStripeWebhookEventCleanupService> _logger;

    public ProcessedStripeWebhookEventCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProcessedStripeWebhookEventCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Events recorded before this are deleted.</summary>
    public static DateTime CutoffFor(DateTime utcNow) => utcNow.AddDays(-RetentionDays);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await DeleteExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deleting old Stripe webhook events failed. Will retry at the next interval.");
            }
        }
    }

    private async Task DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        // The InMemory provider of the test hosts cannot run a bulk delete, and holds nothing worth keeping short.
        if (!scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.IsRelational())
        {
            return;
        }

        var cutoff = CutoffFor(DateTime.UtcNow);
        var deleted = await scope.ServiceProvider.GetRequiredService<IPaymentRepository>()
            .DeleteProcessedStripeEventsBeforeAsync(cutoff, cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} Stripe webhook events recorded before {Cutoff}.", deleted, cutoff);
        }
    }
}
