using System.Net;
using System.Net.Http.Json;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Admin panel S11 (endpoint #66). <c>GET /api/v1/payments/{id}/events</c> over HTTP, and — more to the point — that
/// the rows exist at all, written by the aggregate inside each transition rather than assembled by any one writer.
///
/// <para>
/// Each test drives a real endpoint and then reads the timeline back, so a row appears only if the transition that
/// wrote it also committed. A customer's 403 is in <see cref="Security.NonAdminAuthorizationTests"/>.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class PaymentEventTimelineTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-7";

    private async Task<List<EventRow>> TimelineAsync(Guid paymentId)
    {
        var response = await Client.GetAsync($"/api/v1/payments/{paymentId}/events");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<List<EventRow>>())!;
    }

    /// <summary>
    /// The row is written by <c>PaymentTransaction</c> inside <c>SettleOffline</c>, in the same save as the status
    /// change — so it and the status can only both exist or both not. Nothing is waited for: no outbox poll, no
    /// consumer.
    /// </summary>
    [Test]
    public async Task AnOfflineSettlement_IsOnTheTimeline_AsSoonAsItAnswers()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", amount: 250m);

        await Client.PostAsJsonAsync("/api/v1/payments/offline", new { seeded.OrderId, Reference = "TRF-99" });

        var rows = await TimelineAsync(seeded.Id);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Kind, Is.EqualTo("Transition"));
            Assert.That(rows[0].FromStatus, Is.EqualTo("PENDING"));
            Assert.That(rows[0].ToStatus, Is.EqualTo("SUCCESS"));
            Assert.That(rows[0].Detail, Does.Contain("TRF-99"));
        });
    }

    /// <summary>
    /// The actor comes from `BaseDbContext.SetAuditFields` stamping `CreatedBy` from `ICurrentUserContext`, so no
    /// domain method had to take one. Worth pinning, because the obvious alternative — an `ActorId` threaded through
    /// every transition — is what this avoids, and because the mechanism is one DI registration away from silently
    /// reporting null (Payment was the one service missing it until 2026-09-20).
    /// </summary>
    [Test]
    public async Task AnOperatorsAction_RecordsWhoDidIt()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1");

        await Client.PostAsJsonAsync("/api/v1/payments/offline", new { seeded.OrderId, Reference = "TRF-100" });

        Assert.That((await TimelineAsync(seeded.Id))[0].ActorId, Is.EqualTo(TestUserId));
    }

    /// <summary>
    /// The simulator's settlement runs through <c>POST /api/v1/payments</c>: start, then success, in two saves. Both
    /// rows are there and in order, which no single writer could have produced.
    /// </summary>
    [Test]
    public async Task ASimulatedSettlement_RecordsBothOfItsSteps_InOrder()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", method: PaymentMethodType.Mock);

        var response = await Client.PostAsJsonAsync("/api/v1/payments", new { seeded.OrderId });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());

        var rows = await TimelineAsync(seeded.Id);
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.ToStatus), Is.EqualTo(new[] { "PROCESSING", "SUCCESS" }));
            Assert.That(rows[0].FromStatus, Is.EqualTo("PENDING"));
            Assert.That(rows[1].FromStatus, Is.EqualTo("PROCESSING"));
        });
    }

    /// <summary>A refund and the admin's reason are two rows, and the reason is the one an operator came looking for.</summary>
    [Test]
    public async Task ARefund_RecordsItsReason()
    {
        var seeded = await Factory.SeedPaymentAsync(
            "customer-1", PaymentStatus.Success, method: PaymentMethodType.Mock, intentId: "sim_1");

        var response = await Client.PostAsJsonAsync(
            $"/api/v1/payments/{seeded.Id}/refund", new { Reason = "goods returned, RMA-17" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());

        var rows = await TimelineAsync(seeded.Id);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].ToStatus, Is.EqualTo("REFUNDED"));
            Assert.That(rows[1].Detail, Does.Contain("RMA-17"));
        });
    }

    /// <summary>
    /// The timeline reads oldest first. It is a narrative, and the ordering is the query service's, so a change there
    /// cannot be masked by the handler.
    /// </summary>
    [Test]
    public async Task TheTimeline_IsOldestFirst()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", method: PaymentMethodType.Mock);
        await Client.PostAsJsonAsync("/api/v1/payments", new { seeded.OrderId });

        var rows = await TimelineAsync(seeded.Id);

        Assert.That(rows.Select(r => r.OccurredAt), Is.Ordered);
    }

    [Test]
    public async Task APaymentNothingHasHappenedTo_HasAnEmptyTimeline()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1");

        Assert.That(await TimelineAsync(seeded.Id), Is.Empty);
    }

    /// <summary>
    /// An unknown id must not answer <c>[]</c>: that is indistinguishable from the case above, so an operator who
    /// pasted the wrong id would be told the payment is quiet.
    /// </summary>
    [Test]
    public async Task AnUnknownPayment_Is404_NotAnEmptyTimeline()
    {
        var response = await Client.GetAsync($"/api/v1/payments/{Guid.NewGuid()}/events");

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("PAYMENT_NOT_FOUND"));
        });
    }

    /// <summary>One payment's timeline is its own; the FK and the filter must agree about that.</summary>
    [Test]
    public async Task ATimeline_HoldsOnlyItsOwnPaymentsRows()
    {
        var mine = await Factory.SeedPaymentAsync("customer-1");
        var theirs = await Factory.SeedPaymentAsync("customer-2");
        await Client.PostAsJsonAsync("/api/v1/payments/offline", new { mine.OrderId, Reference = "TRF-A" });
        await Client.PostAsJsonAsync("/api/v1/payments/offline", new { theirs.OrderId, Reference = "TRF-B" });

        var rows = await TimelineAsync(mine.Id);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Detail, Does.Contain("TRF-A"));
        });
    }

    /// <summary>
    /// Every row after a payment's first is appended to an <b>already-persisted</b> payment, which is precisely the
    /// case <c>ValueGeneratedNever()</c> exists for: without it EF issues an UPDATE for the new child, matches no row,
    /// and the endpoint answers 409 (BUG-02's shape).
    /// </summary>
    [Test]
    public async Task AppendingToAPersistedPayment_DoesNotConflict()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", method: PaymentMethodType.Mock);

        var settle = await Client.PostAsJsonAsync("/api/v1/payments", new { seeded.OrderId });
        var refund = await Client.PostAsJsonAsync($"/api/v1/payments/{seeded.Id}/refund", new { Reason = "changed mind" });

        Assert.Multiple(async () =>
        {
            Assert.That(settle.StatusCode, Is.EqualTo(HttpStatusCode.OK), await settle.Content.ReadAsStringAsync());
            Assert.That(refund.StatusCode, Is.EqualTo(HttpStatusCode.OK), await refund.Content.ReadAsStringAsync());
            Assert.That(await TimelineAsync(seeded.Id), Has.Count.EqualTo(4));
        });
    }

    /// <summary>A refused transition writes nothing, timeline included.</summary>
    [Test]
    public async Task ARefusedTransition_LeavesNoRow()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", PaymentStatus.Success, intentId: "pi_x");

        var response = await Client.PostAsJsonAsync("/api/v1/payments/offline", new { seeded.OrderId, Reference = "TRF-Z" });

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(await TimelineAsync(seeded.Id), Is.Empty);
        });
    }

    private sealed record EventRow(
        Guid Id,
        string Kind,
        string? StripeEventId,
        string? FromStatus,
        string ToStatus,
        string Detail,
        string? ActorId,
        DateTime OccurredAt);
}
