using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Infrastructure.Services;

/// <inheritdoc cref="IFailedStripeWebhookReplayer"/>
public sealed class FailedStripeWebhookReplayer : IFailedStripeWebhookReplayer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FailedStripeWebhookReplayer> _logger;

    public FailedStripeWebhookReplayer(IServiceScopeFactory scopeFactory, ILogger<FailedStripeWebhookReplayer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<FailedStripeWebhookReplayReport> ReplayAsync(
        IReadOnlyCollection<Guid> ids,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        var selected = await SelectAsync(ids, maxRows, cancellationToken);
        var results = new List<FailedStripeWebhookReplayResult>(selected.Count);

        foreach (var id in selected)
        {
            results.Add(await ReplayOneAsync(id, cancellationToken));
        }

        // Ids the caller named that are not outstanding captures. Reported rather than dropped: "I asked for six and
        // six came back" is the only way an operator can tell a typo from a success.
        foreach (var missing in ids.Except(selected))
        {
            results.Add(new FailedStripeWebhookReplayResult(
                missing, null, null, FailedStripeWebhookReplayOutcome.NotFound, null));
        }

        return new FailedStripeWebhookReplayReport(
            results.Count,
            results.Count(r => r.Outcome is FailedStripeWebhookReplayOutcome.Replayed
                or FailedStripeWebhookReplayOutcome.AlreadyProcessed),
            results.Count(r => r.Outcome == FailedStripeWebhookReplayOutcome.Failed),
            await CountOutstandingAsync(cancellationToken),
            results);
    }

    private async Task<List<Guid>> SelectAsync(
        IReadOnlyCollection<Guid> ids,
        int maxRows,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

        var outstanding = db.FailedStripeWebhooks.AsNoTracking().Where(w => w.ReplayedAt == null);
        if (ids.Count > 0)
        {
            outstanding = outstanding.Where(w => ids.Contains(w.Id));
        }

        return await outstanding
            .OrderBy(w => w.FirstSeenAt)
            .ThenBy(w => w.Id)
            .Select(w => w.Id)
            .Take(maxRows)
            .ToListAsync(cancellationToken);
    }

    private async Task<int> CountOutstandingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        return await db.FailedStripeWebhooks.AsNoTracking().CountAsync(w => w.ReplayedAt == null, cancellationToken);
    }

    /// <summary>
    /// One capture, in a scope of its own.
    /// <para>The scope is the point, not tidiness: a row that fails again leaves its DbContext holding rejected
    /// changes, and on Postgres a failed statement inside a transaction makes every later one answer <c>25P02</c>. A
    /// shared context would turn the first poisoned row into a batch-wide failure attributed to whichever row came
    /// next.</para>
    /// </summary>
    private async Task<FailedStripeWebhookReplayResult> ReplayOneAsync(Guid id, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

        var capture = await db.FailedStripeWebhooks.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (capture is null || capture.ReplayedAt is not null)
        {
            return new FailedStripeWebhookReplayResult(
                id, capture?.StripeEventId, capture?.EventType, FailedStripeWebhookReplayOutcome.NotFound, null);
        }

        try
        {
            var parser = scope.ServiceProvider.GetRequiredService<IStripeWebhookEventParser>();
            var processor = scope.ServiceProvider.GetRequiredService<IStripeWebhookProcessor>();

            // ParseTrusted, not Parse: Stripe's signature expires after 300 seconds, so re-verifying a capture is
            // impossible rather than stricter. It is sound because nothing reaches this table until Parse accepted it.
            var result = await processor.ApplyAsync(parser.ParseTrusted(capture.Payload), cancellationToken);

            capture.MarkReplayed(DateTime.UtcNow);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Replayed captured Stripe webhook {EventId} ({EventType}); duplicate {IsDuplicate}, payment found {PaymentFound}.",
                capture.StripeEventId,
                capture.EventType,
                result.IsDuplicate,
                result.PaymentFound);

            return new FailedStripeWebhookReplayResult(
                id,
                capture.StripeEventId,
                capture.EventType,
                result.IsDuplicate
                    ? FailedStripeWebhookReplayOutcome.AlreadyProcessed
                    : FailedStripeWebhookReplayOutcome.Replayed,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replay of captured Stripe webhook {CaptureId} failed again.", id);
            await RecordFailureAsync(id, ex, cancellationToken);
            return new FailedStripeWebhookReplayResult(
                id,
                capture.StripeEventId,
                capture.EventType,
                FailedStripeWebhookReplayOutcome.Failed,
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Records the new error, through a context of its own — the one the replay ran on has just failed a save and
    /// cannot be written to again. Its own failure is swallowed: the replay's answer to the operator matters more than
    /// the attempt counter, and the capture stays outstanding either way.
    /// </summary>
    private async Task RecordFailureAsync(Guid id, Exception failure, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

            var capture = await db.FailedStripeWebhooks.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
            if (capture is null)
            {
                return;
            }

            capture.RecordAnotherFailure($"{failure.GetType().Name}: {failure.Message}", DateTime.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record the failed replay of captured Stripe webhook {CaptureId}.", id);
        }
    }
}
