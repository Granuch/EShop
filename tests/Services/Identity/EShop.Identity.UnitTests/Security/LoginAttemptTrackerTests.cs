using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Security;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace EShop.Identity.UnitTests.Security;

[TestFixture]
public class LoginAttemptTrackerTests
{
    [Test]
    public async Task RecordFailedAttemptAsync_WithRedisMultiplexer_ShouldUseAtomicIncrement()
    {
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        database
            .Setup(x => x.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        database
            .Setup(x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var tracker = new LoginAttemptTracker(
            cache.Object,
            Options.Create(new BruteForceProtectionSettings()),
            Mock.Of<ILogger<LoginAttemptTracker>>(),
            redis.Object);

        await tracker.RecordFailedAttemptAsync("user@test.com", "127.0.0.1", CancellationToken.None);

        database.Verify(
            x => x.StringIncrementAsync(It.IsAny<RedisKey>(), 1, It.IsAny<CommandFlags>()),
            Times.Exactly(2));
        database.Verify(
            x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Test]
    public async Task GetFailedAttemptCountAsync_WithRedisMultiplexer_ShouldReadFromRedis()
    {
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"3");

        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var tracker = new LoginAttemptTracker(
            cache.Object,
            Options.Create(new BruteForceProtectionSettings()),
            Mock.Of<ILogger<LoginAttemptTracker>>(),
            redis.Object);

        var count = await tracker.GetFailedAttemptCountAsync("user@test.com", CancellationToken.None);

        Assert.That(count, Is.EqualTo(3));
    }

    // -------------------------------------------------------------------------------------------
    // F-28: the throttle delay runs from the last failure. It used to refuse every attempt once
    // the counter reached the threshold, with no clock involved: the account stayed blocked until
    // the counter's 15-minute TTL, the right password was refused throughout, "wait 2 seconds"
    // meant nothing, and the 5-failure lockout could never be reached.
    // -------------------------------------------------------------------------------------------

    private const string Email = "victim@test.com";
    private const string Ip = "10.0.0.1";
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private static (LoginAttemptTracker Tracker, ManualClock Clock, IDistributedCache Cache) CacheBackedTracker()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var clock = new ManualClock(Start);
        var tracker = new LoginAttemptTracker(
            cache,
            Options.Create(new BruteForceProtectionSettings()),
            Mock.Of<ILogger<LoginAttemptTracker>>(),
            redis: null,
            timeProvider: clock);
        return (tracker, clock, cache);
    }

    private static async Task FailAsync(LoginAttemptTracker tracker, int times)
    {
        for (var i = 0; i < times; i++)
            await tracker.RecordFailedAttemptAsync(Email, Ip, CancellationToken.None);
    }

    [Test]
    public async Task AfterThreeFailures_AnAttemptWithinTheDelay_IsThrottledForTheRemainingTime()
    {
        var (tracker, clock, _) = CacheBackedTracker();
        await FailAsync(tracker, 3);

        clock.Advance(TimeSpan.FromMilliseconds(500));
        var early = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(1));
        var later = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(early.IsAllowed, Is.False);
            Assert.That(early.BlockReason, Is.EqualTo(BlockReason.Throttled));
            Assert.That(early.ThrottleDelaySeconds, Is.EqualTo(2), "1.5 s left of the 2 s delay, rounded up");
            Assert.That(early.Message, Does.Contain("wait 2 seconds"));
            Assert.That(later.ThrottleDelaySeconds, Is.EqualTo(1), "the reported wait counts down");
        });
    }

    [Test]
    public async Task AfterThreeFailures_AnAttemptOnceTheDelayHasPassed_IsAllowed()
    {
        var (tracker, clock, _) = CacheBackedTracker();
        await FailAsync(tracker, 3);

        clock.Advance(TimeSpan.FromSeconds(2) + TimeSpan.FromMilliseconds(1));
        var result = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);

        Assert.That(result.IsAllowed, Is.True,
            "waiting out the advertised delay must give the caller a real attempt (F-28)");
    }

    [Test]
    public async Task AFailureAfterTheDelay_StartsALongerDelayFromThatFailure()
    {
        var (tracker, clock, _) = CacheBackedTracker();
        await FailAsync(tracker, 3);
        clock.Advance(TimeSpan.FromSeconds(3));
        await FailAsync(tracker, 1);

        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.BlockReason, Is.EqualTo(BlockReason.Throttled));
            Assert.That(result.ThrottleDelaySeconds, Is.EqualTo(3), "4 s delay after the 4th failure, 1 s already gone");
        });
    }

    [Test]
    public async Task WaitingOutEachDelay_TheFifthFailureLocksTheAccount()
    {
        // Drives the tracker the way LoginCommandHandler does: validate, and record a failure only
        // for an attempt that was allowed. Under the old count-only throttle the fourth attempt was
        // refused however long the caller waited, so the lockout threshold was unreachable.
        var (tracker, clock, _) = CacheBackedTracker();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var result = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);
            if (!result.IsAllowed)
            {
                Assert.That(result.BlockReason, Is.EqualTo(BlockReason.Throttled), $"attempt {attempt}");
                clock.Advance(TimeSpan.FromSeconds(result.ThrottleDelaySeconds!.Value));
                result = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);
            }

            Assert.That(result.IsAllowed, Is.True, $"attempt {attempt} after waiting the advertised delay");
            await FailAsync(tracker, 1);
        }

        var afterFifth = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);
        Assert.That(afterFifth.BlockReason, Is.EqualTo(BlockReason.AccountLocked));
    }

    [Test]
    public async Task ACounterWithNoLastFailureTimestamp_DoesNotBlock()
    {
        // Counters written before the timestamp key existed: there is no delay that can be shown to
        // be running, so the attempt goes through (and would be counted if it failed).
        var (tracker, _, cache) = CacheBackedTracker();
        await cache.SetStringAsync(
            $"bf_protection:account_attempts:{IdentifierHasher.HashShort(Email)}", "3");

        var result = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);

        Assert.That(result.IsAllowed, Is.True);
    }

    [Test]
    public async Task ValidateAttemptAsync_WithRedis_ThrottlesFromTheStoredLastFailure()
    {
        var now = Start;
        var hashed = IdentifierHasher.HashShort(Email);
        var lastFailureMs = now.AddMilliseconds(-1000).ToUnixTimeMilliseconds();

        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringGetAsync(
                It.Is<RedisKey>(k => k == $"bf_protection:account_attempts:{hashed}"), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"3");
        database
            .Setup(x => x.StringGetAsync(
                It.Is<RedisKey>(k => k == $"bf_protection:account_last_failure:{hashed}"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() => (RedisValue)lastFailureMs.ToString());

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

        var tracker = new LoginAttemptTracker(
            new Mock<IDistributedCache>(MockBehavior.Strict).Object,
            Options.Create(new BruteForceProtectionSettings()),
            Mock.Of<ILogger<LoginAttemptTracker>>(),
            redis.Object,
            new ManualClock(now));

        var oneSecondIn = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);
        lastFailureMs = now.AddMilliseconds(-2500).ToUnixTimeMilliseconds();
        var pastTheDelay = await tracker.ValidateAttemptAsync(Email, Ip, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(oneSecondIn.ThrottleDelaySeconds, Is.EqualTo(1));
            Assert.That(pastTheDelay.IsAllowed, Is.True);
        });
    }

    [Test]
    public async Task RecordAndReset_WithRedis_WriteAndDeleteTheLastFailureKey()
    {
        var hashed = IdentifierHasher.HashShort(Email);
        var lastFailureKey = $"bf_protection:account_last_failure:{hashed}";

        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

        var tracker = new LoginAttemptTracker(
            new Mock<IDistributedCache>(MockBehavior.Strict).Object,
            Options.Create(new BruteForceProtectionSettings()),
            Mock.Of<ILogger<LoginAttemptTracker>>(),
            redis.Object,
            new ManualClock(Start));

        await tracker.RecordFailedAttemptAsync(Email, Ip, CancellationToken.None);
        await tracker.ResetAccountAttemptsAsync(Email, CancellationToken.None);

        // Matched by method name and arguments rather than by a Setup, because StringSetAsync has
        // several overloads across StackExchange.Redis versions.
        var set = database.Invocations.Where(i => i.Method.Name == nameof(IDatabase.StringSetAsync)
            && i.Arguments[0].ToString() == lastFailureKey).ToList();
        var deleted = database.Invocations
            .Where(i => i.Method.Name == nameof(IDatabase.KeyDeleteAsync) && i.Arguments[0] is RedisKey[])
            .SelectMany(i => (RedisKey[])i.Arguments[0])
            .Select(k => k.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(set, Has.Count.EqualTo(1));
            Assert.That(set[0].Arguments[1].ToString(), Is.EqualTo(Start.ToUnixTimeMilliseconds().ToString()));
            Assert.That(deleted, Does.Contain(lastFailureKey), "unlock and a successful login must clear it too");
        });
    }
}
