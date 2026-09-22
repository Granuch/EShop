using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.Infrastructure.Admin;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Admin;

/// <summary>
/// Admin panel S14 (#78, #79) on real Redis: the basket walk is <c>SCAN</c> with a cursor, never <c>KEYS</c>; a page
/// never exceeds its size and never drops a key to stay under it; each request does a bounded amount of scanning; and
/// "abandoned" is a strict cutoff on the basket's own <c>LastModifiedAt</c>.
///
/// <para>The command-count assertions read Redis's own <c>INFO commandstats</c>. That is server-wide, and sound here
/// because this assembly runs its tests one at a time against a container no other suite uses.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class BasketAdminScanTests
{
    private const string CartsUrl = "/api/v1/basket/admin/carts";
    private const string AbandonedUrl = "/api/v1/basket/admin/abandoned";

    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

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

    /// <summary>A stored basket document, written the way the repository writes one.</summary>
    private Task SeedBasketAsync(string userId, DateTime lastModifiedAt, int quantity = 1, decimal price = 10m)
        => _database.StringSetAsync($"basket:user:{userId}", JsonSerializer.Serialize(new
        {
            userId,
            items = new[]
            {
                new { productId = Guid.NewGuid(), productName = "Mug", price, quantity, addedAt = lastModifiedAt }
            },
            createdAt = lastModifiedAt,
            lastModifiedAt
        }, Camel));

    private async Task<List<string>> SeedBasketsAsync(int count)
    {
        var users = Enumerable.Range(0, count).Select(i => $"user-{i:D4}").ToList();
        foreach (var user in users)
        {
            await SeedBasketAsync(user, DateTime.UtcNow);
        }

        return users;
    }

    /// <summary>
    /// Keys the walk must scan past and never return: price sync's index, idempotency markers, checkout markers and the
    /// outbox all share the keyspace with the baskets.
    /// </summary>
    private async Task SeedNoiseAsync(int count)
    {
        var batch = _database.CreateBatch();
        var writes = new List<Task>(count + 2);
        for (var i = 0; i < count; i++)
        {
            writes.Add((i % 3) switch
            {
                0 => batch.SetAddAsync($"basket:product:{Guid.NewGuid()}:users", "user-x"),
                1 => batch.StringSetAsync($"basket:consumer:processed:{Guid.NewGuid()}", "1"),
                _ => batch.StringSetAsync($"basket:checkout:completed:noise-{i}", "1")
            });
        }

        writes.Add(batch.ListLeftPushAsync("basket:outbox:dead", "{}"));
        writes.Add(batch.ListLeftPushAsync("basket:corrupt:user-x", "garbage"));
        batch.Execute();
        await Task.WhenAll(writes);
    }

    private static async Task<JsonElement> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Follows <c>nextCursor</c> to the end, checking every page against its bound on the way.</summary>
    private async Task<List<string>> WalkAsync(string url, int pageSize)
    {
        var users = new List<string>();
        string? cursor = null;

        for (var request = 0; request < 1000; request++)
        {
            var separator = url.Contains('?') ? '&' : '?';
            var page = await PageAsync(_admin, cursor is null
                ? $"{url}{separator}pageSize={pageSize}"
                : $"{url}{separator}pageSize={pageSize}&cursor={Uri.EscapeDataString(cursor)}");

            var items = page.GetProperty("items");
            items.GetArrayLength().Should().BeLessThanOrEqualTo(pageSize, "a page must never exceed the size asked for");
            users.AddRange(items.EnumerateArray().Select(i => i.GetProperty("userId").GetString()!));

            var next = page.GetProperty("nextCursor");
            if (next.ValueKind == JsonValueKind.Null)
            {
                return users;
            }

            cursor = next.GetString();
        }

        throw new AssertionException("the walk never reached a null nextCursor");
    }

    private async Task<long> CommandCallsAsync(string command)
    {
        // INFO is an admin command to StackExchange.Redis, and the host's own connection is deliberately not one.
        await using var admin = await ConnectionMultiplexer.ConnectAsync(_factory.RedisConnectionString + ",allowAdmin=true");
        var server = admin.GetServer(admin.GetEndPoints()[0]);
        var stats = (await server.InfoAsync("commandstats")).SelectMany(group => group);

        foreach (var (key, value) in stats)
        {
            if (key == $"cmdstat_{command}")
            {
                // "calls=12,usec=345,usec_per_call=..."
                var calls = value.Split(',')[0];
                return long.Parse(calls["calls=".Length..]);
            }
        }

        return 0;
    }

    // ---------- /carts ----------

    /// <summary>
    /// Every key fits in one SCAN batch here, so each page after the first resumes part-way through that batch — the
    /// offset half of the cursor. Without it a page would either overrun its size or silently drop the rest of the batch.
    /// </summary>
    [Test]
    public async Task EveryBasket_IsListedExactlyOnce_InPagesThatNeverExceedTheirSize()
    {
        var users = await SeedBasketsAsync(25);
        await SeedNoiseAsync(50);

        var listed = await WalkAsync(CartsUrl, pageSize: 7);

        listed.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(users,
            "each basket once, and nothing that is not a basket — no index, marker, outbox or corrupt-copy key");
    }

    /// <summary>Enough keys that the walk needs many SCAN calls, so the cursor half of the position is what advances.</summary>
    [Test]
    public async Task AWalkAcrossManyScanCalls_StillFindsEveryBasketOnce()
    {
        var users = await SeedBasketsAsync(30);
        await SeedNoiseAsync(3000);

        var listed = await WalkAsync(CartsUrl, pageSize: 100);

        listed.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(users);
    }

    /// <summary>
    /// The plan's one named risk for S14. <c>KEYS</c> would block the server for the whole keyspace; the walk must be
    /// <c>SCAN</c>, and one request must stop after <see cref="RedisBasketAdminReader.MaxScanCallsPerPage"/> calls even
    /// when it has found nothing, handing back a cursor instead of carrying on through the keyspace.
    /// </summary>
    [Test]
    public async Task ARequest_ScansABoundedAmount_AndNeverUsesKeys()
    {
        await SeedNoiseAsync(6000);
        await SeedBasketAsync("the-only-basket", DateTime.UtcNow);
        var keysBefore = await CommandCallsAsync("keys");
        var scansBefore = await CommandCallsAsync("scan");

        var first = await PageAsync(_admin, $"{CartsUrl}?pageSize=100");

        var scans = await CommandCallsAsync("scan") - scansBefore;
        scans.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(RedisBasketAdminReader.MaxScanCallsPerPage);

        if (first.GetProperty("items").GetArrayLength() == 0)
        {
            first.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.String,
                "a request that ran out of scanning before finding anything must say where to carry on");
        }

        (await WalkAsync(CartsUrl, pageSize: 100)).Should().Equal("the-only-basket");
        (await CommandCallsAsync("keys")).Should().Be(keysBefore, "KEYS blocks Redis for the whole keyspace");
    }

    /// <summary>
    /// The case the batch fingerprint exists for. A basket already returned is checked out mid-walk, which removes a key
    /// from the batch the walk is part-way through; an offset counted in the old batch now points one key too far, and
    /// the basket just after it would never be listed. The walk must notice and re-read the batch — a repeat is allowed
    /// (SCAN's own contract), a basket that was there all along going missing is not.
    /// </summary>
    [Test]
    public async Task ABasketDeletedMidWalk_DoesNotCauseAnotherToBeSkipped()
    {
        var users = await SeedBasketsAsync(20);

        var first = await PageAsync(_admin, $"{CartsUrl}?pageSize=5");
        var listed = first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("userId").GetString()!).ToList();
        await _database.KeyDeleteAsync($"basket:user:{listed[0]}");

        var cursor = first.GetProperty("nextCursor").GetString()!;
        for (var request = 0; cursor is not null && request < 100; request++)
        {
            var page = await PageAsync(_admin, $"{CartsUrl}?pageSize=5&cursor={Uri.EscapeDataString(cursor)}");
            listed.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("userId").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
        }

        listed.Should().Contain(users.Skip(1), "every basket stored for the whole walk must be listed at least once");
    }

    [Test]
    public async Task WithNoPageSize_APageHoldsTwenty()
    {
        await SeedBasketsAsync(25);

        var page = await PageAsync(_admin, CartsUrl);

        page.GetProperty("items").GetArrayLength().Should().Be(20);
        page.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Test]
    public async Task ASummary_CarriesTheBasketsFigures_NotItsLines()
    {
        var product = _factory.Catalog.Add("Mug", 12.5m);
        using (var owner = _factory.CreateClientFor("buyer-1"))
        {
            (await owner.PostAsJsonAsync("/api/v1/basket/buyer-1/items", new { productId = product, quantity = 3 }))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var summary = (await PageAsync(_admin, CartsUrl)).GetProperty("items").EnumerateArray().Single();

        summary.GetProperty("userId").GetString().Should().Be("buyer-1");
        summary.GetProperty("isReadable").GetBoolean().Should().BeTrue();
        summary.GetProperty("lines").GetInt32().Should().Be(1);
        summary.GetProperty("totalItems").GetInt64().Should().Be(3);
        summary.GetProperty("totalPrice").GetDecimal().Should().Be(37.5m);
        summary.GetProperty("currency").GetString().Should().Be("USD");
        summary.GetProperty("lastModifiedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        summary.TryGetProperty("items", out _).Should().BeFalse("the lines are GET /api/v1/basket/{userId}'s job");
    }

    /// <summary>
    /// A document the basket cannot be read from is still a key under the prefix. The customer sees an empty basket; the
    /// admin must see that something is there and broken, not have it silently skipped.
    /// </summary>
    [Test]
    public async Task AnUnreadableBasket_IsListed_AsUnreadable()
    {
        await _database.StringSetAsync("basket:user:broken-1", "{ this is not a basket");

        var summary = (await PageAsync(_admin, CartsUrl)).GetProperty("items").EnumerateArray().Single();

        summary.GetProperty("userId").GetString().Should().Be("broken-1");
        summary.GetProperty("isReadable").GetBoolean().Should().BeFalse();
        summary.GetProperty("totalPrice").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [TestCase("pageSize=0")]
    [TestCase("pageSize=101")]
    [TestCase("cursor=abc")]
    [TestCase("cursor=-1")]
    [TestCase("cursor=12-")]
    [TestCase("cursor=12-x")]
    [TestCase("cursor=12-3")]
    [TestCase("cursor=12-3-zzzzzzzzzzzzzzzz")]
    [TestCase("cursor=12-3-00ff")]
    public async Task AnUnusablePageRequest_IsRefused(string query)
    {
        var response = await _admin.GetAsync($"{CartsUrl}?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString()
            .Should().Be("Validation.Failed");
    }

    // ---------- /abandoned ----------

    [Test]
    public async Task OnlyBasketsUnchangedForLongerThanTheCutoff_AreAbandoned()
    {
        var now = DateTime.UtcNow;
        await SeedBasketAsync("three-days", now.AddDays(-3));
        await SeedBasketAsync("thirty-hours", now.AddHours(-30));
        await SeedBasketAsync("one-hour", now.AddHours(-1));
        await _database.StringSetAsync("basket:user:broken-1", "{ unreadable");

        var byDefault = await WalkAsync(AbandonedUrl, pageSize: 10);
        var twoDays = await WalkAsync($"{AbandonedUrl}?olderThan=2d", pageSize: 10);
        var thirtyMinutes = await WalkAsync($"{AbandonedUrl}?olderThan=30m", pageSize: 10);

        byDefault.Should().BeEquivalentTo(new[] { "three-days", "thirty-hours" }, "the default is 24h");
        twoDays.Should().Equal("three-days");
        thirtyMinutes.Should().BeEquivalentTo(new[] { "three-days", "thirty-hours", "one-hour" },
            "an unreadable basket has no age to judge, so it is never abandoned");
    }

    [Test]
    public async Task TheCutoffIsReported_AndIsNowLessTheAge()
    {
        var page = await PageAsync(_admin, $"{AbandonedUrl}?olderThan=6h");

        page.GetProperty("modifiedBefore").GetDateTime()
            .Should().BeCloseTo(DateTime.UtcNow.AddHours(-6), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// "Older than" is strict, pinned at the boundary with a fixed clock: a basket changed exactly at the cutoff is not
    /// yet abandoned, one a tick earlier is.
    /// </summary>
    [Test]
    public async Task ABasketChangedExactlyAtTheCutoff_IsNotYetAbandoned()
    {
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var cutoff = now.UtcDateTime.AddHours(-24);
        await SeedBasketAsync("at-the-cutoff", cutoff);
        await SeedBasketAsync("a-tick-before", cutoff.AddTicks(-1));

        using var clocked = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FixedClock(now))));
        using var admin = clocked.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", BasketApiFactory.CreateToken("ops-1", isAdmin: true));

        var page = await PageAsync(admin, AbandonedUrl);

        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("userId").GetString())
            .Should().Equal("a-tick-before");
        page.GetProperty("modifiedBefore").GetDateTime().Should().Be(cutoff);
    }

    /// <summary>A basket stops being abandoned the moment its customer changes it.</summary>
    [Test]
    public async Task ChangingAnAbandonedBasket_TakesItOffTheList()
    {
        await SeedBasketAsync("returning-1", DateTime.UtcNow.AddDays(-2));
        (await WalkAsync(AbandonedUrl, pageSize: 10)).Should().Equal("returning-1");

        var product = _factory.Catalog.Add("Pen", 1m);
        using (var owner = _factory.CreateClientFor("returning-1"))
        {
            (await owner.PostAsJsonAsync("/api/v1/basket/returning-1/items", new { productId = product, quantity = 1 }))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await WalkAsync(AbandonedUrl, pageSize: 10)).Should().BeEmpty();
    }

    [TestCase("24")]
    [TestCase("0h")]
    [TestCase("31d")]
    [TestCase("1w")]
    [TestCase("h")]
    [TestCase("-5h")]
    [TestCase("999999999d")]
    public async Task AnUnusableAge_IsRefused(string olderThan)
    {
        var response = await _admin.GetAsync($"{AbandonedUrl}?olderThan={Uri.EscapeDataString(olderThan)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The paging rules are shared with /carts through one helper; this is what shows /abandoned applies them.</summary>
    [TestCase("pageSize=101")]
    [TestCase("cursor=abc")]
    public async Task TheAbandonedList_RefusesTheSamePageRequestsTheCartListDoes(string query)
    {
        (await _admin.GetAsync($"{AbandonedUrl}?{query}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
