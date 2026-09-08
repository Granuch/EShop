using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Outbox;

/// <summary>
/// TEST-06. Identity runs with <c>UseOutbox =&gt; true</c>, meaning every integration event it
/// publishes goes through <c>outbox_messages</c> rather than straight to MassTransit — and
/// <b>nothing asserted that before Stage 8</b>. A regression that stopped writing outbox rows
/// would have been completely silent: the endpoint still returns 200, the transaction still
/// commits, and the only symptom is that the downstream service never hears about it.
///
/// <para>
/// These run against real PostgreSQL like the rest of the suite, so the row is genuinely persisted
/// through <c>TransactionBehavior</c>'s commit rather than merely tracked.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class OutboxIntegrationTests : IntegrationTestBase
{
    private const string ForgotPasswordEndpoint = "/api/v1/auth/forgot-password";

    private static readonly string PasswordResetEventType =
        typeof(PasswordResetRequestedIntegrationEvent).FullName!;

    private async Task<List<OutboxMessage>> ReadOutboxAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>
    /// Returns only the rows <paramref name="action"/> itself added.
    ///
    /// <para>
    /// These tests used to read the whole table and assert <c>ContainSingle</c>, which silently
    /// depended on starting from an empty outbox — true only while every test got its own database.
    /// Under the fixture-scoped host (PERF-02) the fixture shares one database, so the previous
    /// test's row was still there and the assertions became order-dependent. Diffing is the better
    /// assertion regardless: what each test actually means is "this request wrote a row", not "the
    /// table contains one row".
    /// </para>
    /// </summary>
    private async Task<List<OutboxMessage>> RowsAddedByAsync(Func<Task> action)
    {
        var before = (await ReadOutboxAsync()).Select(m => m.Id).ToHashSet();
        await action();
        return (await ReadOutboxAsync()).Where(m => !before.Contains(m.Id)).ToList();
    }

    private Task<List<OutboxMessage>> RowsAddedByForgotPasswordAsync(string email)
        => RowsAddedByAsync(() => Client.PostAsJsonAsync(ForgotPasswordEndpoint, new { Email = email }));

    [Test]
    public async Task ForgotPassword_WritesAnOutboxRow_RatherThanPublishingDirectly()
    {
        HttpResponseMessage? response = null;

        var added = await RowsAddedByAsync(async () =>
            response = await Client.PostAsJsonAsync(
                ForgotPasswordEndpoint, new { Email = TestUsers.RegularUser.Email }));

        response!.StatusCode.Should().Be(HttpStatusCode.OK);
        added.Should().ContainSingle(m => m.Type == PasswordResetEventType,
            "the handler enqueues through IIntegrationEventOutbox and TransactionBehavior's commit persists it");
    }

    /// <summary>
    /// The row must land <c>Pending</c> and unprocessed. If it were written already-processed the
    /// processor's <c>ProcessedOnUtc IS NULL</c> query would skip it and the event would never be
    /// delivered — a failure mode with no error anywhere.
    /// </summary>
    [Test]
    public async Task TheOutboxRowStartsPendingSoTheProcessorWillPickItUp()
    {
        var message = (await RowsAddedByForgotPasswordAsync(TestUsers.RegularUser.Email))
            .Single(m => m.Type == PasswordResetEventType);

        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.ProcessedOnUtc.Should().BeNull();
        message.RetryCount.Should().Be(0);
    }

    /// <summary>
    /// SEC-05, stated plainly: a <b>pending</b> row still contains the live reset token. That is
    /// deliberate — the payload is what makes redelivery work, and redaction only happens once the
    /// row becomes terminal — but it means the exposure window is real, just short. Asserting it
    /// here keeps the tradeoff visible instead of leaving people to assume the token is never
    /// written down.
    /// </summary>
    [Test]
    public async Task APendingRowStillCarriesItsPayload_WhichIsWhyRedactionHappensOnDispatch()
    {
        var message = (await RowsAddedByForgotPasswordAsync(TestUsers.RegularUser.Email))
            .Single(m => m.Type == PasswordResetEventType);

        message.Payload.Should().NotBe(OutboxMessage.RedactedPayload);
        message.Payload.Should().Contain("resetToken",
            "a pending payload is intact by design; OutboxProcessorService redacts it the moment the row goes terminal");
    }

    /// <summary>
    /// The enumeration-safe path. An unknown address gets the same 200 and the same body, and it
    /// must also write no outbox row — otherwise the row itself becomes the oracle that tells an
    /// attacker the address exists.
    /// </summary>
    [Test]
    public async Task ForgotPasswordForAnUnknownEmail_WritesNoOutboxRow()
    {
        HttpResponseMessage? response = null;

        var added = await RowsAddedByAsync(async () =>
            response = await Client.PostAsJsonAsync(
                ForgotPasswordEndpoint, new { Email = $"nobody-{Guid.NewGuid():N}@test.com" }));

        response!.StatusCode.Should().Be(HttpStatusCode.OK, "the response must not reveal whether the address exists");
        added.Should().BeEmpty("an outbox row for an unknown address is itself an oracle that the address exists");
    }

    /// <summary>
    /// Correlation ids are what tie an outbox row back to the request that produced it; without
    /// one, a stuck row cannot be traced to anything.
    /// </summary>
    [Test]
    public async Task TheOutboxRowRecordsTheEventTypeAndOccurrenceTime()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);

        var message = (await RowsAddedByForgotPasswordAsync(TestUsers.RegularUser.Email))
            .Single(m => m.Type == PasswordResetEventType);

        message.Id.Should().NotBe(Guid.Empty);
        message.OccurredOnUtc.Should().BeAfter(before);
    }
}
