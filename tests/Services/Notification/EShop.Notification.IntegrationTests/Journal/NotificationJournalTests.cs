using System.Net;
using System.Text.Json;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Notification.IntegrationTests.Journal;

/// <summary>
/// The admin delivery journal over HTTP (Admin panel S12, endpoints #70, #71, #77).
///
/// <para>
/// One host for the fixture, seeded once: every test here reads, none writes, so sharing costs nothing and building a
/// host per test would triple the runtime. The host is Testing, therefore EF InMemory — which is why the
/// <c>from</c>/<c>to</c> assertions below use windows around "now" rather than chosen dates.
/// <c>BaseDbContext</c> overwrites <c>CreatedAt</c> on every insert, and InMemory has no <c>ExecuteUpdate</c> to age a
/// row with afterwards. <c>Persistence/NotificationJournalSqlTests</c> is where seeded dates and the UTC coercion are
/// tested, on a real database.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class NotificationJournalTests
{
    private NotificationApiFactory _factory = null!;
    private HttpClient _admin = null!;

    private Guid _sentId;
    private Guid _failedId;
    private Guid _pendingId;
    private Guid _sendingId;
    private Guid _undeliverableId;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        _factory = new NotificationApiFactory();
        _admin = _factory.CreateAdminClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        _sentId = Add(db, Sent("OrderCreatedEvent", "OrderConfirmation", "user-1", "buyer@eshop.test"));
        _failedId = Add(db, Failed("PaymentFailedEvent", "PaymentFailed", "user-2", "other@eshop.test"));
        _pendingId = Add(db, Pending("OrderShippedEvent", "OrderShipped", "user-1"));
        // An attempt in flight. Seeded deliberately: "queued" means Pending OR Sending, and without a Sending row
        // narrowing that definition to Pending alone would change no number here.
        _sendingId = Add(db, Sending("PaymentCompletedEvent", "PaymentCompleted", "user-4", "inflight@eshop.test"));
        _undeliverableId = Add(db, Undeliverable("PaymentRefundedEvent", "PaymentRefunded", "user-3"));

        await db.SaveChangesAsync();
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        _admin.Dispose();
        _factory.Dispose();
    }

    // ---------- #70: the list ----------

    [Test]
    public async Task TheJournal_ListsEverySeededNotification()
    {
        var page = await GetJsonAsync(_admin, "/api/v1/notifications");

        page.GetProperty("totalCount").GetInt32().Should().Be(5);
        Ids(page).Should().BeEquivalentTo(new[] { _sentId, _failedId, _pendingId, _sendingId, _undeliverableId });
    }

    [Test]
    public async Task TheStatusFilter_IsAUnion_NotAnIntersection()
    {
        var page = await GetJsonAsync(_admin, "/api/v1/notifications?status=Failed&status=Undeliverable");

        Ids(page).Should().BeEquivalentTo(new[] { _failedId, _undeliverableId });
    }

    [TestCase("?eventType=ordercreatedevent")]
    [TestCase("?templateName=ORDERCONFIRMATION")]
    [TestCase("?email=BUYER@ESHOP.TEST")]
    public async Task TextFilters_MatchCaseInsensitively(string queryString)
    {
        // Case-insensitively on purpose: an operator pasting an address out of a mail client or a log gets the casing
        // the source used, not the casing the column happens to hold.
        var page = await GetJsonAsync(_admin, "/api/v1/notifications" + queryString);

        Ids(page).Should().Equal(_sentId);
    }

    [Test]
    public async Task TheUserFilter_NarrowsToThatUsersNotifications()
    {
        var page = await GetJsonAsync(_admin, "/api/v1/notifications?userId=user-1");

        Ids(page).Should().BeEquivalentTo(new[] { _sentId, _pendingId });
    }

    [Test]
    public async Task TheHasErrorFilter_SplitsTheJournalInTwo()
    {
        var withError = Ids(await GetJsonAsync(_admin, "/api/v1/notifications?hasError=true"));
        var withoutError = Ids(await GetJsonAsync(_admin, "/api/v1/notifications?hasError=false"));

        withError.Should().BeEquivalentTo(new[] { _failedId, _undeliverableId });
        withoutError.Should().BeEquivalentTo(new[] { _sentId, _pendingId, _sendingId });
    }

    [Test]
    public async Task TheDateWindow_Narrows()
    {
        var inside = await GetJsonAsync(
            _admin, $"/api/v1/notifications?from={Iso(DateTime.UtcNow.AddHours(-1))}");
        var after = await GetJsonAsync(
            _admin, $"/api/v1/notifications?to={Iso(DateTime.UtcNow.AddHours(-1))}");

        inside.GetProperty("totalCount").GetInt32().Should().Be(5);
        after.GetProperty("totalCount").GetInt32().Should().Be(0, "every seeded row was created just now");
    }

    [Test]
    public async Task ThePage_IsBoundedAndReportsTheTotalUnderTheSameFilter()
    {
        var page = await GetJsonAsync(_admin, "/api/v1/notifications?pageSize=2&pageNumber=1");

        page.GetProperty("totalCount").GetInt32().Should().Be(5, "the count is of matches, not of the page");
        page.GetProperty("items").GetArrayLength().Should().Be(2);
        page.GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task ARowOfTheList_CarriesItsStatusByName_NotByStoredNumber()
    {
        // The column is HasConversion<int>(), so the numbers are a storage detail. A client that read 3 and called it
        // "Failed" (it is Sending) would be wrong in the one place an operator is looking for a problem.
        var page = await GetJsonAsync(_admin, $"/api/v1/notifications?userId=user-2");
        var row = page.GetProperty("items")[0];

        row.GetProperty("status").GetString().Should().Be("Failed");
        row.GetProperty("hasError").GetBoolean().Should().BeTrue();
        row.GetProperty("recipientEmail").GetString().Should().Be("other@eshop.test");
    }

    [TestCase("?status=NoSuchStatus")]
    [TestCase("?pageSize=0")]
    [TestCase("?pageSize=101")]
    [TestCase("?pageNumber=0")]
    [TestCase("?from=2026-09-02T00:00:00Z&to=2026-09-01T00:00:00Z")]
    public async Task AnUnusableFilter_IsRefused_RatherThanQuietlyIgnored(string queryString)
    {
        var response = await _admin.GetAsync("/api/v1/notifications" + queryString);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("errorCode").GetString().Should().Be("Validation.Failed");
        problem.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    // ---------- #71: the detail ----------

    [Test]
    public async Task TheDetail_CarriesTheFailureReasonAndCorrelationId_WhichTheListOmits()
    {
        var detail = await GetJsonAsync(_admin, $"/api/v1/notifications/{_failedId}");

        detail.GetProperty("id").GetGuid().Should().Be(_failedId);
        detail.GetProperty("correlationId").GetString().Should().Be("corr-PaymentFailedEvent");
        detail.GetProperty("lastError").GetString().Should().NotBeNullOrEmpty();
        detail.GetProperty("retryCount").GetInt32().Should().Be(1);
        detail.GetProperty("isFinal").GetBoolean().Should().BeFalse("a Failed notification is retried");
    }

    [Test]
    public async Task AnUndeliverableNotification_ReportsItsReasonVerbatim_AndIsFinal()
    {
        // MarkFailed rewrites every reason by keyword (SanitizeError); MarkUndeliverable stores it as it is. Asserting
        // the exact text is what keeps the journal honest about which of the two happened.
        var detail = await GetJsonAsync(_admin, $"/api/v1/notifications/{_undeliverableId}");

        detail.GetProperty("lastError").GetString().Should().Be("The recipient has no email address.");
        detail.GetProperty("isFinal").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task AnUnknownId_Is404_WithTheServicesOwnErrorCode()
    {
        var response = await _admin.GetAsync($"/api/v1/notifications/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("errorCode").GetString().Should().Be("Notification.NotFound");
    }

    [Test]
    public async Task AMalformedId_IsRefusedByRouting_BeforeTheHandler()
    {
        // The {id:guid} constraint fails to match, so routing answers a bare 404. Distinct from the Result-path 404
        // above, and correct.
        var response = await _admin.GetAsync("/api/v1/notifications/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- #77: the stats ----------

    [Test]
    public async Task TheStats_CountSentFailedAndQueued_OverTheWholeJournal()
    {
        var stats = await GetJsonAsync(_admin, "/api/v1/notifications/stats");

        stats.GetProperty("total").GetInt32().Should().Be(5);
        stats.GetProperty("sent").GetInt32().Should().Be(1);
        stats.GetProperty("failed").GetInt32().Should().Be(1);
        stats.GetProperty("undeliverable").GetInt32().Should().Be(1);
        stats.GetProperty("queued").GetInt32().Should().Be(2,
            "queued is Pending OR Sending — one row of each — because neither is final and neither has been delivered");
    }

    [Test]
    public async Task TheStats_NameEveryStatus_EvenThoseWithNoRows()
    {
        // A client charting the breakdown should not have to know which keys the server happened to omit.
        var stats = await GetJsonAsync(_admin, "/api/v1/notifications/stats");

        var names = stats.GetProperty("byStatus").EnumerateArray()
            .Select(s => s.GetProperty("status").GetString())
            .ToArray();

        names.Should().BeEquivalentTo(Enum.GetNames<NotificationStatus>());
        stats.GetProperty("byStatus").EnumerateArray()
            .Sum(s => s.GetProperty("count").GetInt32())
            .Should().Be(stats.GetProperty("total").GetInt32(), "the total is summed from the breakdown, not queried again");
    }

    [TestCase("?status=NoSuchStatus")]
    [TestCase("?from=2026-09-02T00:00:00Z&to=2026-09-01T00:00:00Z")]
    public async Task TheStats_RefuseTheSameUnusableFiltersTheListDoes(string queryString)
    {
        // The two share NotificationFilterRules. Without a request that reaches the stats validator, removing that one
        // call would leave every test here green while the stats endpoint silently accepted a typo as "every status".
        var response = await _admin.GetAsync("/api/v1/notifications/stats" + queryString);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("errorCode").GetString().Should().Be("Validation.Failed");
    }

    [Test]
    public async Task TheStats_HonourTheSameFiltersTheListDoes()
    {
        // The two share one filter record precisely so they cannot drift: a stats page that quietly stopped honouring
        // ?templateName= would still return a perfectly well-formed set of numbers for the wrong rows.
        var stats = await GetJsonAsync(_admin, "/api/v1/notifications/stats?templateName=PaymentFailed");

        stats.GetProperty("total").GetInt32().Should().Be(1);
        stats.GetProperty("failed").GetInt32().Should().Be(1);
        stats.GetProperty("sent").GetInt32().Should().Be(0);
    }

    // ---------- helpers ----------

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static IEnumerable<Guid> Ids(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();

    private static string Iso(DateTime value) => Uri.EscapeDataString(value.ToString("O"));

    private static Guid Add(NotificationDbContext db, NotificationLog log)
    {
        db.NotificationLogs.Add(log);
        return log.Id;
    }

    private static NotificationLog New(string eventType, string template, string userId) =>
        NotificationLog.CreatePending(
            Guid.NewGuid(), eventType, $"corr-{eventType}", userId, template, $"Subject for {eventType}");

    private static NotificationLog Pending(string eventType, string template, string userId)
        => New(eventType, template, userId);

    private static NotificationLog Sent(string eventType, string template, string userId, string email)
    {
        var log = New(eventType, template, userId);
        log.BeginAttempt(DateTime.UtcNow);
        log.RecordRecipient(email);
        log.MarkSent("provider-message-1");
        return log;
    }

    private static NotificationLog Failed(string eventType, string template, string userId, string email)
    {
        var log = New(eventType, template, userId);
        log.BeginAttempt(DateTime.UtcNow);
        log.RecordRecipient(email);
        log.MarkFailed("SMTP connection refused");
        return log;
    }

    private static NotificationLog Sending(string eventType, string template, string userId, string email)
    {
        var log = New(eventType, template, userId);
        log.BeginAttempt(DateTime.UtcNow);
        log.RecordRecipient(email);
        return log;
    }

    private static NotificationLog Undeliverable(string eventType, string template, string userId)
    {
        var log = New(eventType, template, userId);
        log.BeginAttempt(DateTime.UtcNow);
        log.MarkUndeliverable("The recipient has no email address.");
        return log;
    }
}
