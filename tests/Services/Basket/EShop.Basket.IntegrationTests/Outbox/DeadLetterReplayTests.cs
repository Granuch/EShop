using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.Infrastructure.Outbox;
using EShop.Basket.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Messaging.Events;
using FluentAssertions;

namespace EShop.Basket.IntegrationTests.Outbox;

/// <summary>
/// Basket audit S7 (H6, D7). A dead-lettered checkout is an order Ordering never received; it is kept, and an admin can
/// count and replay them. Replay resets the retry count and keeps the payload byte for byte.
/// </summary>
[TestFixture]
[Category("Integration")]
public class DeadLetterReplayTests
{
    private const string DeadLettersUrl = "/api/v1/basket/admin/outbox/dead-letters";

    private static string DeadLetter(Guid id)
        => (RedisOutboxMessage.Parse(RedisOutboxMessage.Serialize(
                new BasketCheckedOutEvent
                {
                    EventId = id,
                    UserId = "user-1",
                    ShippingAddress = "1 Main St/Apt 2, Springfield, IL 62701, US",
                    TotalPrice = 10m
                },
                correlationId: null))! with { RetryCount = 10 }).ToJson();

    [Test]
    public async Task AnAdmin_ReplaysDeadLettersToPending_WithAFreshRetryCountAndTheSamePayload()
    {
        await using var factory = new BasketApiFactory();
        var database = factory.Redis.GetDatabase();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var original = DeadLetter(first);
        await database.ListLeftPushAsync(BasketOutboxKeys.DeadLetter, original);
        await database.ListLeftPushAsync(BasketOutboxKeys.DeadLetter, DeadLetter(second));
        using var admin = factory.CreateClientFor("ops-1", isAdmin: true);

        (await admin.GetFromJsonAsync<JsonElement>(DeadLettersUrl)).GetProperty("count").GetInt64().Should().Be(2);

        var response = await admin.PostAsync($"{DeadLettersUrl}/replay", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetInt32().Should().Be(2);
        (await database.ListLengthAsync(BasketOutboxKeys.DeadLetter)).Should().Be(0);

        var pending = (await database.ListRangeAsync(BasketOutboxKeys.Pending))
            .Select(value => RedisOutboxMessage.Parse(value.ToString())!)
            .ToList();
        pending.Select(m => m.Id).Should().BeEquivalentTo(new[] { first, second });
        pending.Should().OnlyContain(m => m.RetryCount == 0, "a replayed message gets its full set of attempts again");
        pending.Single(m => m.Id == first).Payload.Should().Be(RedisOutboxMessage.Parse(original)!.Payload,
            "the order inside must survive the Lua round trip unchanged");
        (await admin.GetFromJsonAsync<JsonElement>(DeadLettersUrl)).GetProperty("count").GetInt64().Should().Be(0);
    }

    [Test]
    public async Task ACustomer_CanNeitherCountNorReplayDeadLetters()
    {
        await using var factory = new BasketApiFactory();
        await factory.Redis.GetDatabase().ListLeftPushAsync(BasketOutboxKeys.DeadLetter, DeadLetter(Guid.NewGuid()));
        using var customer = factory.CreateClientFor("user-1");

        (await customer.GetAsync(DeadLettersUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.PostAsync($"{DeadLettersUrl}/replay", content: null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.Redis.GetDatabase().ListLengthAsync(BasketOutboxKeys.DeadLetter)).Should().Be(1);
    }
}
