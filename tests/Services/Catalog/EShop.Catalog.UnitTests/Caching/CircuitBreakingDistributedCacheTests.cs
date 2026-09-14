using EShop.Catalog.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EShop.Catalog.UnitTests.Caching;

/// <summary>
/// L28 (Catalog audit Stage 10). The breaker had no tests at all — its cooldown read
/// <c>DateTime.UtcNow</c> directly, so none could be written without sleeping for 30 seconds. With an
/// injected <see cref="TimeProvider"/> each state transition is now pinned.
/// </summary>
[TestFixture]
public class CircuitBreakingDistributedCacheTests
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    private const string Key = "k";

    private Mock<IDistributedCache> _inner = null!;
    private ManualTimeProvider _time = null!;
    private CircuitBreakingDistributedCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _inner = new Mock<IDistributedCache>();
        _time = new ManualTimeProvider();
        _cache = new CircuitBreakingDistributedCache(
            _inner.Object,
            NullLogger<CircuitBreakingDistributedCache>.Instance,
            failureThreshold: 3,
            openDuration: Cooldown,
            timeProvider: _time);
    }

    private void InnerFails() => _inner
        .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("redis down"));

    private void InnerReturns(byte[] value) => _inner
        .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(value);

    private void AssertInnerCalls(int expected) => _inner.Verify(
        c => c.GetAsync(Key, It.IsAny<CancellationToken>()), Times.Exactly(expected));

    private async Task OpenTheCircuitAsync()
    {
        InnerFails();
        for (var i = 0; i < 3; i++)
        {
            Assert.That(await _cache.GetAsync(Key), Is.Null, "a failed read degrades to a miss");
        }
    }

    [Test]
    public async Task AfterThresholdFailures_TheCircuitOpens_AndStopsCallingRedis()
    {
        await OpenTheCircuitAsync();

        await _cache.GetAsync(Key);
        _time.Advance(Cooldown - TimeSpan.FromSeconds(1));
        await _cache.GetAsync(Key);

        AssertInnerCalls(3);
    }

    [Test]
    public async Task AfterTheCooldown_ASuccessfulProbe_ClosesTheCircuit()
    {
        await OpenTheCircuitAsync();
        _time.Advance(Cooldown);
        InnerReturns([1]);

        Assert.That(await _cache.GetAsync(Key), Is.EqualTo(new byte[] { 1 }), "the probe reaches Redis");
        Assert.That(await _cache.GetAsync(Key), Is.EqualTo(new byte[] { 1 }), "and the circuit is closed after it");

        AssertInnerCalls(5);
    }

    [Test]
    public async Task AFailedProbe_KeepsTheCircuitOpenForAnotherFullCooldown()
    {
        await OpenTheCircuitAsync();
        _time.Advance(Cooldown);

        await _cache.GetAsync(Key);                        // the probe, which fails
        AssertInnerCalls(4);

        _time.Advance(Cooldown - TimeSpan.FromSeconds(1));
        await _cache.GetAsync(Key);                        // still inside the new cooldown
        AssertInnerCalls(4);

        _time.Advance(TimeSpan.FromSeconds(2));
        await _cache.GetAsync(Key);                        // the next probe
        AssertInnerCalls(5);
    }

    [Test]
    public async Task WhileAProbeIsInFlight_EveryOtherCallIsSkipped()
    {
        await OpenTheCircuitAsync();
        _time.Advance(Cooldown);

        var pending = new TaskCompletionSource<byte[]?>();
        _inner.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(pending.Task);

        var probe = _cache.GetAsync(Key);

        // Checked before awaiting, never by awaiting: a skipped call completes at once, while a
        // second probe would wait on the same never-completing Redis call — so awaiting it directly
        // turns this regression into a hung test run instead of a red test (which is what the first
        // falsification of this test did).
        var other = _cache.GetAsync(Key);
        Assert.That(other.IsCompleted, Is.True, "a second caller must be skipped, not become a second probe");
        Assert.That(await other, Is.Null);

        pending.SetResult([7]);
        Assert.That(await probe, Is.EqualTo(new byte[] { 7 }));

        AssertInnerCalls(4);
    }

    [Test]
    public async Task ASuccessBeforeTheThreshold_ResetsTheCount()
    {
        InnerFails();
        await _cache.GetAsync(Key);
        await _cache.GetAsync(Key);

        InnerReturns([1]);
        await _cache.GetAsync(Key);

        InnerFails();
        await _cache.GetAsync(Key);
        await _cache.GetAsync(Key);
        await _cache.GetAsync(Key);                        // only the third consecutive failure opens it

        AssertInnerCalls(6);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
