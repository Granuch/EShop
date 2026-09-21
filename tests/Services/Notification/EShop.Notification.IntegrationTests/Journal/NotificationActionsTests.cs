using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.Resend;
using EShop.Notification.IntegrationTests.Fixtures;
using FluentAssertions;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Notification.IntegrationTests.Journal;

/// <summary>
/// Admin panel S13 (#72–#76) over HTTP, on a host whose bus really delivers (<see cref="DeliveringNotificationApiFactory"/>):
/// a resend is followed all the way to the consumer that sends it and the row it records, not just to a 202.
///
/// <para>
/// One host for the fixture; every test seeds its own rows under its own user id, so the batch retry's filter keeps each
/// test to its own rows. The host is EF InMemory — no row version — so what only Postgres can show (an operator's write
/// racing a delivery) is <c>Persistence/NotificationOperatorWriteTests</c>.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class NotificationActionsTests
{
    private DeliveringNotificationApiFactory _factory = null!;
    private HttpClient _admin = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _factory = new DeliveringNotificationApiFactory();
        _admin = _factory.CreateAdminClient();
        await _factory.Services.GetRequiredService<ITestHarness>().Start();
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        _admin.Dispose();
        _factory.Dispose();
    }

    // ---------- #72: resend ----------

    [Test]
    public async Task AResend_OfAFailedNotification_IsDeliveredByItsConsumer_AndRecordedSent()
    {
        var refund = Refund(User());
        var id = await SeedAsync(refund, Fail);

        var response = await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location!.OriginalString.Should().Be($"/api/v1/notifications/{id}");
        var body = await ReadAsync(response);
        body.GetProperty("status").GetString().Should().Be("Failed",
            "the 202 answers the row as it stands; the delivery happens in the consumer, after");

        var sent = await WaitForStatusAsync(id, NotificationStatus.Sent);
        sent.RetryCount.Should().Be(1, "the earlier failed attempt stays counted; the resend's attempt succeeded");
        sent.RecipientEmail.Should().Be(DeliveringNotificationApiFactory.CustomerEmail);
        SendsFor(refund.OrderId).Should().ContainSingle()
            .Which.Method.Should().Be(nameof(DeliveringNotificationApiFactory.RecordingEmailService.SendPaymentRefundedAsync));
    }

    private static IEnumerable<TestCaseData> EveryResendableEvent()
    {
        yield return Case(new OrderCreatedEvent { OrderId = Guid.NewGuid(), UserId = "u", Items = [new OrderEventItem { Quantity = 1 }] },
            "SendOrderConfirmationAsync");
        yield return Case(new OrderShippedEvent { OrderId = Guid.NewGuid(), UserId = "u", UserEmail = "shipped@eshop.test" },
            "SendOrderShippedAsync");
        yield return Case(new PaymentCreatedEvent { OrderId = Guid.NewGuid(), UserId = "u", Amount = 1 }, "SendPaymentCreatedAsync");
        yield return Case(new PaymentCompletedEvent { OrderId = Guid.NewGuid(), UserId = "u", Amount = 1 }, "SendPaymentCompletedAsync");
        yield return Case(new PaymentFailedEvent { OrderId = Guid.NewGuid(), UserId = "u", Reason = "declined" }, "SendPaymentFailedAsync");
        yield return Case(new PaymentRefundedEvent { OrderId = Guid.NewGuid(), UserId = "u", Amount = 1 }, "SendPaymentRefundedAsync");

        static TestCaseData Case(IntegrationEvent evt, string method) => new TestCaseData(evt, method).SetArgDisplayNames(evt.GetType().Name);
    }

    /// <summary>
    /// The queue a resend is sent to is derived, not listed; this proves each derived name is the queue the real consumer
    /// is bound to, by the only evidence that counts — that consumer sent the email. A wrong name would leave the message
    /// in a queue nothing reads, and the row Failed for ever with a 202 on record.
    /// </summary>
    [TestCaseSource(nameof(EveryResendableEvent))]
    public async Task EveryResendableType_IsDeliveredFromItsConsumersOwnQueue(IntegrationEvent evt, string expectedMethod)
    {
        var id = await SeedAsync(evt, Fail);

        (await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        await WaitForStatusAsync(id, NotificationStatus.Sent);
        var orderId = (Guid)evt.GetType().GetProperty("OrderId")!.GetValue(evt)!;
        SendsFor(orderId).Should().ContainSingle().Which.Method.Should().Be(expectedMethod);
    }

    [Test]
    public async Task ASentNotification_CannotBeResent()
    {
        var refund = Refund(User());
        var id = await SeedAsync(refund, log => { log.BeginAttempt(DateTime.UtcNow); log.MarkSent("<m@test>"); });

        var response = await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null);

        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, NotificationErrors.FinalCode);
        (await FindAsync(id)).Status.Should().Be(NotificationStatus.Sent);
        SendsFor(refund.OrderId).Should().BeEmpty("a customer who has the email must not be sent it again");
    }

    [Test]
    public async Task AnUndeliverableNotification_CannotBeResent()
    {
        var id = await SeedAsync(Refund(User()), log => { log.BeginAttempt(DateTime.UtcNow); log.MarkUndeliverable("no such user"); });

        await ShouldBeProblemAsync(
            await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null), HttpStatusCode.Conflict, NotificationErrors.FinalCode);
    }

    [Test]
    public async Task AnAttemptHoldingItsLease_IsLeftToFinish()
    {
        var refund = Refund(User());
        var id = await SeedAsync(refund, log => log.BeginAttempt(DateTime.UtcNow));

        var response = await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null);

        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, NotificationErrors.AttemptInProgressCode);
        (await FindAsync(id)).Status.Should().Be(NotificationStatus.Sending);
        SendsFor(refund.OrderId).Should().BeEmpty();
    }

    [Test]
    public async Task AnAttemptPastItsLease_IsResent_AndTheConsumerTakesItOver()
    {
        var id = await SeedAsync(Refund(User()),
            log => log.BeginAttempt(DateTime.UtcNow - NotificationLog.AttemptLease - TimeSpan.FromMinutes(1)));

        (await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        await WaitForStatusAsync(id, NotificationStatus.Sent);
    }

    [Test]
    public async Task APasswordReset_CannotBeResent_BecauseItsEventWasNeverKept()
    {
        var id = await SeedAsync(
            new PasswordResetRequestedIntegrationEvent { UserId = User(), ResetToken = "live-token" }, Fail);

        var response = await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null);

        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, NotificationErrors.NotResendableCode);
    }

    [Test]
    public async Task ARowWrittenBeforeThePayloadColumn_CannotBeResent()
    {
        var id = await SeedAsync(Refund(User()), Fail, keepPayload: false);

        await ShouldBeProblemAsync(
            await _admin.PostAsync($"/api/v1/notifications/{id}/resend", null), HttpStatusCode.Conflict, NotificationErrors.NotResendableCode);
    }

    [Test]
    public async Task AResendOfANotificationThatDoesNotExist_IsNotFound()
    {
        await ShouldBeProblemAsync(
            await _admin.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/resend", null),
            HttpStatusCode.NotFound,
            NotificationErrors.NotFoundCode);
    }

    [Test]
    public async Task TheDetail_SaysWhetherANotificationCanBeResent()
    {
        var resendable = await SeedAsync(Refund(User()), Fail);
        var reset = await SeedAsync(new PasswordResetRequestedIntegrationEvent { UserId = User(), ResetToken = "t" }, Fail);

        (await GetAsync($"/api/v1/notifications/{resendable}")).GetProperty("isResendable").GetBoolean().Should().BeTrue();
        (await GetAsync($"/api/v1/notifications/{reset}")).GetProperty("isResendable").GetBoolean().Should().BeFalse();
    }

    // ---------- #73: retry-failed ----------

    [Test]
    public async Task ABatchRetry_SendsTheFailedNotificationsThatKeptTheirEvent_AndNothingElse()
    {
        var user = User();
        var failed = new[]
        {
            await SeedAsync(Refund(user), Fail),
            await SeedAsync(Refund(user), Fail),
            await SeedAsync(Refund(user), Fail)
        };
        var noPayload = await SeedAsync(Refund(user), Fail, keepPayload: false);
        var sent = await SeedAsync(Refund(user), log => { log.BeginAttempt(DateTime.UtcNow); log.MarkSent(null); });
        var otherUsers = await SeedAsync(Refund(User()), Fail);

        var response = await _admin.PostAsJsonAsync("/api/v1/notifications/retry-failed", new { userId = user });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var report = await ReadAsync(response);
        report.GetProperty("matching").GetInt32().Should().Be(3);
        Guids(report, "dispatchedIds").Should().BeEquivalentTo(failed);
        Guids(report, "failedIds").Should().BeEmpty();

        foreach (var id in failed)
        {
            await WaitForStatusAsync(id, NotificationStatus.Sent);
        }

        (await FindAsync(noPayload)).Status.Should().Be(NotificationStatus.Failed);
        (await FindAsync(sent)).Status.Should().Be(NotificationStatus.Sent);
        (await FindAsync(otherUsers)).Status.Should().Be(NotificationStatus.Failed, "the filter narrows the batch");
    }

    [Test]
    public async Task ABatchRetry_IsBounded_AndTakesTheOldestFirst()
    {
        var user = User();
        var oldest = await SeedAsync(Refund(user), Fail);
        await Task.Delay(20);
        var middle = await SeedAsync(Refund(user), Fail);
        await Task.Delay(20);
        var newest = await SeedAsync(Refund(user), Fail);

        var response = await _admin.PostAsJsonAsync("/api/v1/notifications/retry-failed", new { userId = user, limit = 2 });

        var report = await ReadAsync(response);
        report.GetProperty("matching").GetInt32().Should().Be(3, "the caller learns there is more to do");
        report.GetProperty("limit").GetInt32().Should().Be(2);
        Guids(report, "dispatchedIds").Should().Equal(oldest, middle);

        await WaitForStatusAsync(middle, NotificationStatus.Sent);
        (await FindAsync(newest)).Status.Should().Be(NotificationStatus.Failed);
    }

    // ---------- #75 / #76: templates ----------

    [Test]
    public async Task TheTemplates_AreListed_WithWhichOnesCanBeResent()
    {
        var templates = (await GetAsync("/api/v1/notifications/templates")).EnumerateArray().ToList();

        templates.Should().HaveCount(7);
        templates.Where(t => !t.GetProperty("resendable").GetBoolean())
            .Select(t => t.GetProperty("name").GetString())
            .Should().Equal("password-reset");
    }

    [Test]
    public async Task ATestSend_SendsTheTemplateToTheGivenAddress_AndWritesNoJournalRow()
    {
        var rowsBefore = await CountRowsAsync();

        var response = await _admin.PostAsJsonAsync(
            "/api/v1/notifications/templates/ORDER-CREATED/test", new { email = "ops@eshop.test", name = "Ops" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.GetProperty("templateName").GetString().Should().Be("order-created", "the catalog's own spelling");
        body.GetProperty("providerMessageId").GetString().Should().NotBeNullOrEmpty();
        _factory.Email.Sends.Should().Contain(s => s.Recipient == "ops@eshop.test" && s.Method == "SendOrderConfirmationAsync");
        (await CountRowsAsync()).Should().Be(rowsBefore, "the journal is the record of what customers were sent");
    }

    [Test]
    public async Task ATestSendTheMailServerRefuses_Is503_AndLeaksNoProviderText()
    {
        var response = await _admin.PostAsJsonAsync(
            "/api/v1/notifications/templates/order-created/test",
            new { email = DeliveringNotificationApiFactory.UnreachableSmtpAddress });

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.ServiceUnavailable, NotificationErrors.TestSendFailedCode);
        problem.GetProperty("detail").GetString().Should().NotContainAny("10061", "actively refused", "Socket");
    }

    [Test]
    public async Task ATestSendOfAnUnknownTemplate_IsNotFound()
    {
        await ShouldBeProblemAsync(
            await _admin.PostAsJsonAsync("/api/v1/notifications/templates/no-such-template/test", new { email = "ops@eshop.test" }),
            HttpStatusCode.NotFound,
            NotificationErrors.TemplateNotFoundCode);
    }

    [Test]
    public async Task ATestSendToAnUnusableAddress_IsRefusedBeforeAnythingIsSent()
    {
        await ShouldBeProblemAsync(
            await _admin.PostAsJsonAsync("/api/v1/notifications/templates/order-created/test", new { email = "not-an-address" }),
            HttpStatusCode.BadRequest,
            "Validation.Failed");
    }

    // ---------- helpers ----------

    private static string User() => $"user-{Guid.NewGuid():N}";

    private static PaymentRefundedEvent Refund(string userId) => new()
    {
        EventId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        UserId = userId,
        PaymentIntentId = "pi_s13",
        Amount = 10m,
        RefundedAt = DateTime.UtcNow
    };

    private static void Fail(NotificationLog log)
    {
        log.BeginAttempt(DateTime.UtcNow);
        log.MarkFailed("Simulated SMTP failure");
    }

    /// <summary>A row as the consumer would have created it — payload included, unless it predates the column.</summary>
    private async Task<Guid> SeedAsync(IntegrationEvent evt, Action<NotificationLog> prepare, bool keepPayload = true)
    {
        var payload = keepPayload
            ? (string?)typeof(NotificationPayload).GetMethod(nameof(NotificationPayload.Serialize))!
                .MakeGenericMethod(evt.GetType()).Invoke(null, [evt])
            : null;

        var log = NotificationLog.CreatePending(
            evt.EventId, evt.GetType().Name, "corr-s13", evt.GetType().GetProperty("UserId")?.GetValue(evt) as string,
            "seeded", "seeded subject", payload);
        prepare(log);

        using var scope = _factory.Services.CreateScope();
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

    private async Task<int> CountRowsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().NotificationLogs.CountAsync();
    }

    /// <summary>Polls, bounded: the consumer runs on the bus's own threads, after the 202.</summary>
    private async Task<NotificationLog> WaitForStatusAsync(Guid id, NotificationStatus status)
    {
        var deadline = DateTime.UtcNow + DeliveringNotificationApiFactory.HarnessTimeout;
        NotificationLog log;
        do
        {
            log = await FindAsync(id);
            if (log.Status == status)
            {
                return log;
            }

            await Task.Delay(50);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"notification {id} is {log.Status} after {DeliveringNotificationApiFactory.HarnessTimeout}, not {status}");
        return log;
    }

    private IEnumerable<(string Method, string Recipient, Guid? OrderId)> SendsFor(Guid orderId)
        => _factory.Email.Sends.Where(s => s.OrderId == orderId);

    private async Task<JsonElement> GetAsync(string path)
    {
        var response = await _admin.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static IEnumerable<Guid> Guids(JsonElement body, string property)
        => body.GetProperty(property).EnumerateArray().Select(e => e.GetGuid()).ToList();

    internal static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, text);
        var problem = JsonDocument.Parse(text).RootElement.Clone();
        problem.GetProperty("errorCode").GetString().Should().Be(errorCode, text);
        return problem;
    }
}
