using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.Resend;
using EShop.Notification.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EShop.Notification.IntegrationTests.Journal.NotificationActionsTests;

namespace EShop.Notification.IntegrationTests.Journal;

/// <summary>
/// Admin panel S13 on the plain Testing host, which runs no bus — as Development does when RabbitMQ is not configured.
/// Mark-undeliverable needs no bus at all; a resend must say it has nowhere to go rather than fail to resolve a handler.
/// Also home to the request-shape failures, which never reach a bus either way.
/// </summary>
[TestFixture]
[Category("Integration")]
public class NotificationActionsWithoutABusTests
{
    private NotificationApiFactory _factory = null!;
    private HttpClient _admin = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _factory = new NotificationApiFactory();
        _admin = _factory.CreateAdminClient();
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        _admin.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task AResend_WithNoBus_Is503_NotAnError()
    {
        var id = await SeedAsync(_factory, Failed(payload: true));

        await ShouldBeProblemAsync(
            await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null),
            HttpStatusCode.ServiceUnavailable,
            NotificationErrors.BusUnavailableCode);
    }

    [Test]
    public async Task ABatchRetry_WithNoBus_Is503()
    {
        await ShouldBeProblemAsync(
            await _admin.PostAsync("/api/v1/notifications/retry-failed", null),
            HttpStatusCode.ServiceUnavailable,
            NotificationErrors.BusUnavailableCode);
    }

    [TestCase(0)]
    [TestCase(RetryLimitOverTheCap)]
    public async Task ABatchRetryOutsideItsBound_IsRefused(int limit)
    {
        await ShouldBeProblemAsync(
            await _admin.PostAsJsonAsync("/api/v1/notifications/retry-failed", new { limit }),
            HttpStatusCode.BadRequest,
            "Validation.Failed");
    }

    private const int RetryLimitOverTheCap =
        EShop.Notification.Application.Notifications.Commands.RetryFailedNotifications.RetryFailedNotificationsCommand.MaxRetryPerRequest + 1;

    // ---------- #74: mark-undeliverable ----------

    [Test]
    public async Task AFailedNotification_CanBeMarkedUndeliverable_AndIsThenFinal()
    {
        var id = await SeedAsync(_factory, Failed(payload: true));

        var response = await _admin.PostAsJsonAsync(
            $"/api/v1/notifications/{id}/mark-undeliverable", new { reason = "Mailbox disabled per bounce report." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().Should().Be("Undeliverable");
        body.GetProperty("isFinal").GetBoolean().Should().BeTrue();
        body.GetProperty("isResendable").GetBoolean().Should().BeFalse();

        var stored = await FindAsync(id);
        stored.Status.Should().Be(NotificationStatus.Undeliverable, "the effect, not just the status code");
        stored.LastError.Should().Be(NotificationLog.OperatorReasonPrefix + "Mailbox disabled per bounce report.");

        // And final means final: a resend is now refused for that reason, before the missing bus is even considered.
        await ShouldBeProblemAsync(
            await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null), HttpStatusCode.Conflict, NotificationErrors.FinalCode);
    }

    [Test]
    public async Task ASentNotification_CannotBeMarkedUndeliverable()
    {
        var sent = NotificationLog.CreatePending(Guid.NewGuid(), nameof(PaymentRefundedEvent), null, "u", "t", "s");
        sent.BeginAttempt(DateTime.UtcNow);
        sent.MarkSent(null);
        var id = await SeedAsync(_factory, sent);

        await ShouldBeProblemAsync(
            await _admin.PostAsJsonAsync($"/api/v1/notifications/{id}/mark-undeliverable", new { reason = "x" }),
            HttpStatusCode.Conflict,
            NotificationErrors.FinalCode);
        (await FindAsync(id)).Status.Should().Be(NotificationStatus.Sent);
    }

    [Test]
    public async Task AnAttemptHoldingItsLease_CannotBeMarkedUndeliverable()
    {
        var sending = NotificationLog.CreatePending(Guid.NewGuid(), nameof(PaymentRefundedEvent), null, "u", "t", "s");
        sending.BeginAttempt(DateTime.UtcNow);
        var id = await SeedAsync(_factory, sending);

        await ShouldBeProblemAsync(
            await _admin.PostAsJsonAsync($"/api/v1/notifications/{id}/mark-undeliverable", new { reason = "x" }),
            HttpStatusCode.Conflict,
            NotificationErrors.AttemptInProgressCode);
        (await FindAsync(id)).Status.Should().Be(NotificationStatus.Sending);
    }

    [Test]
    public async Task MarkingUndeliverable_WithoutAReason_IsRefused()
    {
        var id = await SeedAsync(_factory, Failed(payload: false));

        await ShouldBeProblemAsync(
            await _admin.PostAsJsonAsync($"/api/v1/notifications/{id}/mark-undeliverable", new { reason = "  " }),
            HttpStatusCode.BadRequest,
            "Validation.Failed");
        (await FindAsync(id)).Status.Should().Be(NotificationStatus.Failed);
    }

    [Test]
    public async Task MarkingUndeliverable_ANotificationThatDoesNotExist_IsNotFound()
    {
        await ShouldBeProblemAsync(
            await _admin.PostAsJsonAsync($"/api/v1/notifications/{Guid.NewGuid()}/mark-undeliverable", new { reason = "x" }),
            HttpStatusCode.NotFound,
            NotificationErrors.NotFoundCode);
    }

    /// <summary>
    /// ThrowOnBadRequest + AddMalformedJsonBody: without them a broken body is a bare 400 with nothing in it outside
    /// Development, and a client cannot tell it from any other refusal.
    /// </summary>
    [Test]
    public async Task AMalformedBody_IsAProblemNamingTheRequest_NotABare400()
    {
        var id = await SeedAsync(_factory, Failed(payload: false));

        var response = await _admin.PostAsync(
            $"/api/v1/notifications/{id}/mark-undeliverable",
            new StringContent("{\"reason\": ", Encoding.UTF8, "application/json"));

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ProblemErrorCodes.MalformedRequest);
    }

    // ---------- helpers ----------

    internal static NotificationLog Failed(bool payload)
    {
        var refund = new PaymentRefundedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "u", Amount = 1 };
        var log = NotificationLog.CreatePending(
            refund.EventId, nameof(PaymentRefundedEvent), null, "u", "payment-refunded", "s",
            payload ? NotificationPayload.Serialize(refund) : null);
        log.BeginAttempt(DateTime.UtcNow);
        log.MarkFailed("Simulated SMTP failure");
        return log;
    }

    internal static async Task<Guid> SeedAsync(NotificationApiFactory factory, NotificationLog log)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync();
        return log.Id;
    }

    private async Task<NotificationLog> FindAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>()
            .NotificationLogs.AsNoTracking().SingleAsync(x => x.Id == id);
    }
}

/// <summary>
/// Admin panel S13. A delivery that claims the row between the operator's read and save makes the save match no row.
/// That must be a 409 the operator can act on, not a 500 — which is what it was until this stage registered
/// <c>AddEfConcurrency()</c>: S12's reads could raise no such exception, so the branch was deliberately absent. The race
/// itself is real only on Postgres (<c>Persistence/NotificationOperatorWriteTests</c>); here the repository simply
/// reports it, which is enough to pin the mapping.
/// </summary>
[TestFixture]
[Category("Integration")]
public class NotificationOperatorWriteConflictTests
{
    [Test]
    public async Task AnOperatorWriteThatLosesToADelivery_Is409_NotA500()
    {
        await using var factory = new ConflictingSaveFactory();
        using var admin = factory.CreateAdminClient();
        var id = await NotificationActionsWithoutABusTests.SeedAsync(factory, NotificationActionsWithoutABusTests.Failed(payload: false));

        var response = await admin.PostAsJsonAsync($"/api/v1/notifications/{id}/mark-undeliverable", new { reason = "closing it" });

        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, ProblemErrorCodes.ConcurrencyConflict);
    }

    private sealed class ConflictingSaveFactory : NotificationApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                var registered = services.Single(d => d.ServiceType == typeof(INotificationLogRepository));
                services.Remove(registered);
                services.AddScoped<INotificationLogRepository>(sp => new ConflictingSaves(
                    (INotificationLogRepository)ActivatorUtilities.CreateInstance(sp, registered.ImplementationType!)));
            });
        }
    }

    private sealed class ConflictingSaves(INotificationLogRepository inner) : INotificationLogRepository
    {
        public Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default) => inner.FindByEventIdAsync(eventId, ct);

        public Task<NotificationLog?> FindByIdAsync(Guid id, CancellationToken ct = default) => inner.FindByIdAsync(id, ct);

        public Task<bool> TryAddAsync(NotificationLog log, CancellationToken ct = default) => inner.TryAddAsync(log, ct);

        public Task SaveAsync(NotificationLog log, CancellationToken ct = default)
            => throw new DbUpdateConcurrencyException("Simulated: a delivery claimed the row since it was read.");
    }
}
