using System.Net;
using System.Net.Http.Json;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Webhooks;

/// <summary>
/// Admin panel S11 (endpoint #67): a Stripe delivery this service accepted and then failed to apply is captured, and
/// an operator can replay it.
///
/// <para>
/// Before this stage a failure here answered 500 and the delivery was gone unless Stripe chose to redeliver it — and
/// Stripe's redelivery is neither indefinite nor on demand, so an incident that outlasted it lost payments with no
/// record that anything had been lost.
/// </para>
///
/// <para>
/// The customer's 403 on the replay endpoint is in <see cref="Security.NonAdminAuthorizationTests"/>.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class FailedStripeWebhookTests : AuthenticatedIntegrationTestBase
{
    private const string ReplayEndpoint = "/api/v1/payments/webhooks/failed/replay";

    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-1";

    protected override PaymentApiFactory CreateFactory() => new FailingWebhookPaymentApiFactory();

    private FailingWebhookPaymentApiFactory Webhooks => (FailingWebhookPaymentApiFactory)Factory;

    private async Task<List<FailedStripeWebhook>> CapturesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        return await db.FailedStripeWebhooks.AsNoTracking().ToListAsync();
    }

    private async Task<List<PaymentEvent>> TimelineAsync(Guid paymentId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        return await db.PaymentEvents.AsNoTracking()
            .Where(e => e.PaymentTransactionId == paymentId)
            .ToListAsync();
    }

    private async Task<PaymentTransaction> StoredAsync(PaymentTransaction seeded)
        => (await Factory.FindByOrderIdAsync(seeded.OrderId))!;

    private async Task<(PaymentTransaction Payment, string Payload)> ADeliveryAsync()
    {
        var intent = StripeWebhooks.NewIntentId();
        var payment = await Factory.SeedPaymentAsync("webhook-user", PaymentStatus.Processing, intentId: intent);
        return (payment, StripeWebhooks.Event("payment_intent.succeeded", intent, "succeeded"));
    }

    private Task<HttpResponseMessage> DeliverAsync(string payload)
        => StripeWebhooks.PostAsync(Client, payload, StripeWebhooks.Sign(payload));

    private async Task<ReplayReport> ReplayAsync(object? body = null)
    {
        var response = await Client.PostAsJsonAsync(ReplayEndpoint, body ?? new { });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ReplayReport>())!;
    }

    // ---- capture ---------------------------------------------------------------------------------------------

    [Test]
    public async Task ADeliveryThatFailsInternally_IsCaptured_AndChangesNothing()
    {
        var (payment, payload) = await ADeliveryAsync();

        var response = await DeliverAsync(payload);

        var captures = await CapturesAsync();
        var stored = await StoredAsync(payment);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError),
                "Stripe reads the status code; a 500 is what makes it redeliver");
            Assert.That(captures, Has.Count.EqualTo(1));
            Assert.That(captures[0].Payload, Is.EqualTo(payload), "a replay re-applies these exact bytes");
            Assert.That(captures[0].EventType, Is.EqualTo("payment_intent.succeeded"));
            Assert.That(captures[0].StripeEventId, Is.Not.Null.And.StartWith("evt_"));
            Assert.That(captures[0].AttemptCount, Is.EqualTo(1));
            Assert.That(captures[0].ReplayedAt, Is.Null);
            Assert.That(captures[0].Error, Does.Contain("the database went away mid-webhook"));
        });

        // The half-applied transition must not have reached the database with the capture. This is the round the
        // capture's own scope exists for: written through the request's DbContext, the capture's SaveChanges would
        // commit the payment change that had just been rejected.
        Assert.Multiple(async () =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(await TimelineAsync(payment.Id), Is.Empty);
        });
    }

    /// <summary>
    /// Stripe redelivers a failing event for days. One row per event, with the attempts counted, is the difference
    /// between a table an operator can read and a thousand copies of one incident.
    /// </summary>
    [Test]
    public async Task ARedeliveryOfTheSameFailingEvent_CountsAnAttempt_RatherThanAddingARow()
    {
        var (_, payload) = await ADeliveryAsync();

        await DeliverAsync(payload);
        await DeliverAsync(payload);
        await DeliverAsync(payload);

        var captures = await CapturesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(captures, Has.Count.EqualTo(1));
            Assert.That(captures[0].AttemptCount, Is.EqualTo(3));
            Assert.That(captures[0].LastAttemptAt, Is.GreaterThanOrEqualTo(captures[0].FirstSeenAt));
        });
    }

    [Test]
    public async Task TwoDifferentFailingEvents_AreTwoCaptures()
    {
        var (_, first) = await ADeliveryAsync();
        var (_, second) = await ADeliveryAsync();

        await DeliverAsync(first);
        await DeliverAsync(second);

        Assert.That(await CapturesAsync(), Has.Count.EqualTo(2));
    }

    /// <summary>
    /// The safety argument for replaying a stored payload without re-checking its signature is that nothing reaches
    /// this table until the signature check has passed. A delivery refused for its signature must therefore never be
    /// captured — and that is also why capturing it would be useless: the same bytes can never succeed.
    /// </summary>
    [Test]
    public async Task ADeliveryRefusedForItsSignature_IsNotCaptured()
    {
        var (_, payload) = await ADeliveryAsync();

        var response = await StripeWebhooks.PostAsync(
            Client, payload, StripeWebhooks.Sign(payload, "whsec_someone_else"));

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await CapturesAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task AMalformedPayload_IsNotCaptured()
    {
        const string payload = "{\"nonsense\":true}";

        var response = await StripeWebhooks.PostAsync(Client, payload, StripeWebhooks.Sign(payload));

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await CapturesAsync(), Is.Empty);
        });
    }

    // ---- replay ----------------------------------------------------------------------------------------------

    [Test]
    public async Task AReplay_AppliesTheCapturedDelivery_AndClosesIt()
    {
        var (payment, payload) = await ADeliveryAsync();
        await DeliverAsync(payload);
        Webhooks.FailWhen = _ => false;

        var report = await ReplayAsync();

        var stored = await StoredAsync(payment);
        var captures = await CapturesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(report.Attempted, Is.EqualTo(1));
            Assert.That(report.Replayed, Is.EqualTo(1));
            Assert.That(report.StillFailing, Is.Zero);
            Assert.That(report.Outstanding, Is.Zero);
            Assert.That(report.Results.Single().Outcome, Is.EqualTo("Replayed"));
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Success), "the point of a replay is the payment");
            Assert.That(captures.Single().ReplayedAt, Is.Not.Null);
        });
    }

    /// <summary>
    /// A replay re-applies a delivery Stripe may meanwhile have redelivered successfully. It stays idempotent because
    /// it goes through the same processor the live endpoint uses, which checks <c>ProcessedStripeWebhookEvents</c>
    /// first — the plan's named risk for this stage.
    /// </summary>
    [Test]
    public async Task AReplayOfAnEventThatSucceededMeanwhile_ChangesNothingASecondTime()
    {
        var (payment, payload) = await ADeliveryAsync();
        await DeliverAsync(payload);

        // Stripe's own redelivery gets through.
        Webhooks.FailWhen = _ => false;
        Assert.That((await DeliverAsync(payload)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var afterStripe = await StoredAsync(payment);

        var report = await ReplayAsync();

        var afterReplay = await StoredAsync(payment);
        Assert.Multiple(async () =>
        {
            Assert.That(report.Results.Single().Outcome, Is.EqualTo("AlreadyProcessed"));
            Assert.That(report.Replayed, Is.EqualTo(1), "closed, not failed");
            Assert.That(report.Outstanding, Is.Zero);
            Assert.That(afterReplay.Status, Is.EqualTo(PaymentStatus.Success));
            Assert.That(afterReplay.ProcessedAt, Is.EqualTo(afterStripe.ProcessedAt),
                "the payment must not be settled a second time");
            // One webhook row, from the one delivery that applied. A replay that re-applied would add another.
            Assert.That(await TimelineAsync(payment.Id), Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// Why a replay must not re-check the signature, shown rather than argued.
    ///
    /// <para>Stripe's signature carries the instant it was made and the parser refuses anything older than 300
    /// seconds, so re-verifying a capture is impossible rather than stricter — an operator replaying yesterday's
    /// incident would be told the delivery is forged. The other tests here cannot show it: they capture and replay
    /// inside the tolerance, so a replay that re-verified would pass every one of them. This seeds a capture whose
    /// signature has expired, which is what every real one looks like.</para>
    ///
    /// <para>What makes skipping the check sound is the pair above: a delivery refused for its signature is never
    /// captured, so "captured" already means "verified".</para>
    /// </summary>
    [Test]
    public async Task AReplayOfACaptureSignedLongAgo_IsNotRefusedForItsExpiredSignature()
    {
        var (payment, payload) = await ADeliveryAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            db.FailedStripeWebhooks.Add(FailedStripeWebhook.Capture(
                StripeEventIdOf(payload),
                "payment_intent.succeeded",
                payload,
                StripeWebhooks.Sign(payload, signedAt: DateTimeOffset.UtcNow.AddMinutes(-10)),
                "the database went away mid-webhook",
                DateTime.UtcNow.AddMinutes(-10)));
            await db.SaveChangesAsync();
        }
        Webhooks.FailWhen = _ => false;

        var report = await ReplayAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(report.Results.Single().Outcome, Is.EqualTo("Replayed"));
            Assert.That((await StoredAsync(payment)).Status, Is.EqualTo(PaymentStatus.Success));
        });
    }

    private static string StripeEventIdOf(string payload)
        => System.Text.Json.JsonDocument.Parse(payload).RootElement.GetProperty("id").GetString()!;

    [Test]
    public async Task ReplayingTwice_IsSafe()
    {
        var (_, payload) = await ADeliveryAsync();
        await DeliverAsync(payload);
        Webhooks.FailWhen = _ => false;

        await ReplayAsync();
        var second = await ReplayAsync();

        Assert.Multiple(() =>
        {
            Assert.That(second.Attempted, Is.Zero, "a closed capture is not outstanding");
            Assert.That(second.Outstanding, Is.Zero);
        });
    }

    [Test]
    public async Task AReplayThatFailsAgain_LeavesTheCaptureOutstanding_WithTheNewCount()
    {
        var (_, payload) = await ADeliveryAsync();
        await DeliverAsync(payload);

        var report = await ReplayAsync();

        var capture = (await CapturesAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(report.Replayed, Is.Zero);
            Assert.That(report.StillFailing, Is.EqualTo(1));
            Assert.That(report.Outstanding, Is.EqualTo(1));
            Assert.That(report.Results.Single().Outcome, Is.EqualTo("Failed"));
            Assert.That(report.Results.Single().Error, Does.Contain("the database went away mid-webhook"));
            Assert.That(capture.ReplayedAt, Is.Null);
            Assert.That(capture.AttemptCount, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// One poisoned row must not decide the batch. With a shared unit of work the first failure would leave Postgres
    /// refusing every later statement, so the second row would be reported as a failure of the transaction rather
    /// than of itself.
    /// </summary>
    [Test]
    public async Task OneRowFailingAgain_DoesNotStopTheOthers()
    {
        var (poisoned, poisonedPayload) = await ADeliveryAsync();
        var (good, goodPayload) = await ADeliveryAsync();
        await DeliverAsync(poisonedPayload);
        await DeliverAsync(goodPayload);

        // Only the first payment still fails, so the batch contains one row that throws and one that applies.
        Webhooks.FailWhen = p => p.Id == poisoned.Id;

        var report = await ReplayAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(report.Attempted, Is.EqualTo(2));
            Assert.That(report.Replayed, Is.EqualTo(1));
            Assert.That(report.StillFailing, Is.EqualTo(1));
            Assert.That(report.Outstanding, Is.EqualTo(1));
            Assert.That((await StoredAsync(good)).Status, Is.EqualTo(PaymentStatus.Success),
                "the healthy row must be applied whichever order the batch ran in");
            Assert.That((await StoredAsync(poisoned)).Status, Is.EqualTo(PaymentStatus.Processing));
        });
    }

    [Test]
    public async Task ANamedCapture_IsTheOnlyOneReplayed()
    {
        var (first, firstPayload) = await ADeliveryAsync();
        var (second, secondPayload) = await ADeliveryAsync();
        await DeliverAsync(firstPayload);
        await DeliverAsync(secondPayload);
        var firstCapture = (await CapturesAsync()).Single(c => c.Payload == firstPayload);
        Webhooks.FailWhen = _ => false;

        var report = await ReplayAsync(new { Ids = new[] { firstCapture.Id } });

        Assert.Multiple(async () =>
        {
            Assert.That(report.Attempted, Is.EqualTo(1));
            Assert.That(report.Outstanding, Is.EqualTo(1), "the other capture is untouched");
            Assert.That((await StoredAsync(first)).Status, Is.EqualTo(PaymentStatus.Success));
            Assert.That((await StoredAsync(second)).Status, Is.EqualTo(PaymentStatus.Processing));
        });
    }

    /// <summary>A typo must be visible. Silently replaying nothing and answering "0 attempted" looks like success.</summary>
    [Test]
    public async Task AnIdThatIsNotAnOutstandingCapture_IsReportedAsNotFound()
    {
        var unknown = Guid.NewGuid();

        var report = await ReplayAsync(new { Ids = new[] { unknown } });

        Assert.Multiple(() =>
        {
            Assert.That(report.Results, Has.Count.EqualTo(1));
            Assert.That(report.Results[0].Id, Is.EqualTo(unknown));
            Assert.That(report.Results[0].Outcome, Is.EqualTo("NotFound"));
        });
    }

    [Test]
    public async Task AnEmptyRequest_ReplaysEveryOutstandingCapture()
    {
        var (_, first) = await ADeliveryAsync();
        var (_, second) = await ADeliveryAsync();
        await DeliverAsync(first);
        await DeliverAsync(second);
        Webhooks.FailWhen = _ => false;

        var report = await ReplayAsync();

        Assert.That(report.Attempted, Is.EqualTo(2));
    }

    /// <summary>Risk A8: refused, not truncated — a caller told "100 replayed" cannot find the 400 that were not.</summary>
    [Test]
    public async Task MoreIdsThanTheCap_IsRefused()
    {
        var tooMany = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();

        var response = await Client.PostAsJsonAsync(ReplayEndpoint, new { Ids = tooMany });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private sealed record ReplayReport(
        int Attempted,
        int Replayed,
        int StillFailing,
        int Outstanding,
        IReadOnlyList<ReplayResult> Results);

    private sealed record ReplayResult(Guid Id, string? StripeEventId, string? EventType, string Outcome, string? Error);
}
