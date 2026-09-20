using System.Text.Json;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Infrastructure.Services;

/// <inheritdoc cref="IFailedStripeWebhookStore"/>
public sealed class FailedStripeWebhookStore : IFailedStripeWebhookStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FailedStripeWebhookStore> _logger;

    public FailedStripeWebhookStore(IServiceScopeFactory scopeFactory, ILogger<FailedStripeWebhookStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task CaptureAsync(
        string payload,
        string signatureHeader,
        Exception failure,
        CancellationToken cancellationToken = default)
    {
        var (eventId, eventType) = ReadEventIdentity(payload);
        var error = $"{failure.GetType().Name}: {failure.Message}";

        try
        {
            // Its OWN scope, and therefore its own DbContext and its own connection. The caller's context has just
            // failed a SaveChanges: it still holds the rejected changes, and on Postgres its transaction is aborted,
            // so anything written through it would be lost with the failure this exists to record.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

            var existing = eventId is null
                ? null
                : await db.FailedStripeWebhooks.SingleOrDefaultAsync(
                    w => w.StripeEventId == eventId, cancellationToken);

            var now = DateTime.UtcNow;
            if (existing is null)
            {
                db.FailedStripeWebhooks.Add(FailedStripeWebhook.Capture(
                    eventId, eventType, payload, signatureHeader, error, now));
            }
            else
            {
                existing.RecordAnotherFailure(error, now);
            }

            await db.SaveChangesAsync(cancellationToken);

            _logger.LogError(
                failure,
                "Stripe webhook {EventId} ({EventType}) failed and was captured for replay.",
                eventId,
                eventType);
        }
        catch (Exception captureFailure)
        {
            // Never rethrown. The caller owes Stripe a 500 for the ORIGINAL failure so it redelivers; throwing here
            // would replace that failure with this one and lose it. The read-then-write above can also lose a race
            // between two concurrent deliveries of the same event, and the unique index then rejects the second — a
            // lost capture for an event that already has a row, which is the harmless half of the race.
            _logger.LogError(
                captureFailure,
                "Failed to capture Stripe webhook {EventId} after it errored; the delivery is lost unless Stripe redelivers it.",
                eventId);
        }
    }

    /// <summary>
    /// The event's id and type, read straight from the payload rather than through the parser.
    /// <para>The parser is what just failed to be survivable, and every exception it can raise is one this method must
    /// not propagate; a capture with no id is still worth far more than no capture. Both fields are metadata for the
    /// operator and the dedupe key — the replay re-parses <see cref="FailedStripeWebhook.Payload"/> itself.</para>
    /// </summary>
    private static (string? EventId, string? EventType) ReadEventIdentity(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            return (
                root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
                root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
