using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Outbox;
using EShop.Basket.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Messaging.Events;
using FluentAssertions;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Admin;

/// <summary>
/// Admin panel S14 (#81). Until now an operator could count the dead letters and replay them, and see nothing in
/// between. The details list says which checkouts they are, when and why each died — and never returns the order
/// inside, which carries the customer's shipping address.
/// </summary>
[TestFixture]
[Category("Integration")]
public class DeadLetterDetailsTests
{
    private const string DetailsUrl = "/api/v1/basket/admin/outbox/dead-letters/details";
    private const string ShippingAddress = "742 Evergreen Terrace, Springfield, OR 97403, US";

    private BasketApiFactory _factory = null!;
    private IDatabase _database = null!;
    private HttpClient _admin = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new BasketApiFactory();
        _database = _factory.Redis.GetDatabase();
        _admin = _factory.CreateClientFor("ops-1", isAdmin: true);
    }

    [TearDown]
    public void TearDown()
    {
        _admin.Dispose();
        _factory.Dispose();
    }

    private static RedisOutboxMessage Envelope(Guid id, DateTime occurredOn) => RedisOutboxMessage.Parse(
        RedisOutboxMessage.Serialize(
            new BasketCheckedOutEvent
            {
                EventId = id,
                OccurredOn = occurredOn,
                UserId = "customer-7",
                ShippingAddress = ShippingAddress,
                TotalPrice = 10m
            },
            correlationId: "corr-" + id.ToString("N")))!;

    /// <summary>Dead letters are pushed on the left, as the processor does, so the last pushed is the newest.</summary>
    private Task PushAsync(string raw) => _database.ListLeftPushAsync(BasketOutboxKeys.DeadLetter, raw);

    private async Task<JsonElement> DetailsAsync(string query = "")
    {
        var response = await _admin.GetAsync(DetailsUrl + query);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Test]
    public async Task EachDeadLetter_SaysWhichCheckoutItIs_WhenAndWhyItDied()
    {
        var id = Guid.NewGuid();
        var occurredOn = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        var deadAt = new DateTime(2026, 9, 20, 12, 30, 0, DateTimeKind.Utc);
        await PushAsync((Envelope(id, occurredOn) with
        {
            RetryCount = 10,
            FailureReason = OutboxDeadLetterReasons.PublishFailed,
            ExceptionType = "BrokerUnreachableException",
            DeadLetteredAtUtc = deadAt
        }).ToJson());

        var letter = (await DetailsAsync()).GetProperty("items").EnumerateArray().Single();

        letter.GetProperty("isReadable").GetBoolean().Should().BeTrue();
        letter.GetProperty("messageId").GetGuid().Should().Be(id, "it is the checkoutId the customer was given");
        letter.GetProperty("eventType").GetString().Should().Be(typeof(BasketCheckedOutEvent).FullName);
        letter.GetProperty("occurredOnUtc").GetDateTime().Should().Be(occurredOn);
        letter.GetProperty("deadLetteredAtUtc").GetDateTime().Should().Be(deadAt);
        letter.GetProperty("attempts").GetInt32().Should().Be(10);
        letter.GetProperty("error").GetString().Should().Be("PublishFailed");
        letter.GetProperty("exceptionType").GetString().Should().Be("BrokerUnreachableException");
        letter.GetProperty("correlationId").GetString().Should().Be("corr-" + id.ToString("N"));
    }

    /// <summary>
    /// Asserted by value, not by field name: a check for a <c>payload</c> property would pass while the address sat in a
    /// field called something else (the lesson of Admin panel S6's token-hash round).
    /// </summary>
    [Test]
    public async Task TheOrderInside_IsNeverReturned()
    {
        await PushAsync((Envelope(Guid.NewGuid(), DateTime.UtcNow) with { RetryCount = 10 }).ToJson());

        var body = await _admin.GetStringAsync(DetailsUrl);

        body.Should().NotContain("Evergreen").And.NotContain("customer-7").And.NotContain("\"payload\"");
    }

    /// <summary>
    /// Everything dead-lettered before S14 has no reason or time recorded, and an entry the processor could not parse is
    /// kept exactly as it was found. Neither may break the list, and neither may be dressed up with invented values.
    /// </summary>
    [Test]
    public async Task OlderAndUnreadableEntries_AreListedHonestly()
    {
        var old = Guid.NewGuid();
        await PushAsync((Envelope(old, DateTime.UtcNow) with { RetryCount = 5 }).ToJson());
        await PushAsync("not json at all");
        await PushAsync((Envelope(Guid.NewGuid(), DateTime.UtcNow) with
        {
            RetryCount = RedisOutboxMessage.UnpublishableRetryCount,
            FailureReason = OutboxDeadLetterReasons.Unpublishable
        }).ToJson());

        var items = (await DetailsAsync()).GetProperty("items").EnumerateArray().ToList();

        items.Should().HaveCount(3);

        items[0].GetProperty("error").GetString().Should().Be("Unpublishable");
        items[0].GetProperty("attempts").ValueKind.Should().Be(JsonValueKind.Null,
            "an unpublishable message was never attempted; its stored count is a sentinel, not int.MaxValue attempts");

        items[1].GetProperty("isReadable").GetBoolean().Should().BeFalse();
        items[1].GetProperty("messageId").ValueKind.Should().Be(JsonValueKind.Null);

        items[2].GetProperty("messageId").GetGuid().Should().Be(old);
        items[2].GetProperty("attempts").GetInt32().Should().Be(5);
        items[2].GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        items[2].GetProperty("deadLetteredAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task ThePage_IsNewestFirst_AndTheTotalIsTheWholeList()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            await PushAsync((Envelope(id, DateTime.UtcNow) with { RetryCount = 10 }).ToJson());
        }

        var page = await DetailsAsync("?offset=1&limit=2");

        page.GetProperty("total").GetInt64().Should().Be(5);
        page.GetProperty("offset").GetInt32().Should().Be(1);
        page.GetProperty("limit").GetInt32().Should().Be(2);
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("messageId").GetGuid())
            .Should().Equal(ids[3], ids[2]);
    }

    [Test]
    public async Task WithNoLimit_APageHoldsTwenty()
    {
        for (var i = 0; i < 25; i++)
        {
            await PushAsync((Envelope(Guid.NewGuid(), DateTime.UtcNow) with { RetryCount = 10 }).ToJson());
        }

        var page = await DetailsAsync();

        page.GetProperty("items").GetArrayLength().Should().Be(20);
        page.GetProperty("total").GetInt64().Should().Be(25);
    }

    /// <summary>Reading is not replaying: the list is exactly as it was.</summary>
    [Test]
    public async Task ReadingTheDetails_MovesNothing()
    {
        await PushAsync((Envelope(Guid.NewGuid(), DateTime.UtcNow) with { RetryCount = 10 }).ToJson());

        await DetailsAsync();

        (await _database.ListLengthAsync(BasketOutboxKeys.DeadLetter)).Should().Be(1);
        (await _database.ListLengthAsync(BasketOutboxKeys.Pending)).Should().Be(0);
    }

    [TestCase("?limit=0")]
    [TestCase("?limit=101")]
    [TestCase("?offset=-1")]
    public async Task AnUnusablePageRequest_IsRefused(string query)
    {
        var response = await _admin.GetAsync(DetailsUrl + query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString()
            .Should().Be("Validation.Failed");
    }
}
