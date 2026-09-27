using EShop.Identity.Infrastructure.Security;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Security;

/// <summary>
/// One confirmation resend per account per cooldown, whoever asks — and a cache outage lets the
/// resend through rather than stranding a user who never received their link.
/// </summary>
[TestFixture]
public class EmailConfirmationResendThrottleTests
{
    private static EmailConfirmationResendThrottle Throttle(IDistributedCache cache)
        => new(cache, Mock.Of<ILogger<EmailConfirmationResendThrottle>>());

    private static IDistributedCache MemoryCache()
        => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    [Test]
    public async Task TheFirstResend_IsAllowed_AndTheNextIsRefusedInsideTheCooldown()
    {
        var throttle = Throttle(MemoryCache());

        Assert.That(await throttle.TryAcquireAsync("user-1"), Is.True);
        Assert.That(await throttle.TryAcquireAsync("user-1"), Is.False);
    }

    [Test]
    public async Task TheCooldown_IsPerAccount()
    {
        var throttle = Throttle(MemoryCache());
        await throttle.TryAcquireAsync("user-1");

        Assert.That(await throttle.TryAcquireAsync("user-2"), Is.True);
    }

    [Test]
    public async Task TheCooldownKey_ExpiresOnItsOwn_AfterTheCooldown()
    {
        var cache = new Mock<IDistributedCache>();
        // Explicit: a loose Moq mock answers an empty byte[] rather than null, which reads as
        // "already in cooldown" and the key is never written.
        cache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        DistributedCacheEntryOptions? written = null;
        cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, _, o, _) => written = o)
            .Returns(Task.CompletedTask);

        await Throttle(cache.Object).TryAcquireAsync("user-1");

        Assert.That(written?.AbsoluteExpirationRelativeToNow, Is.EqualTo(EmailConfirmationResendThrottle.Cooldown),
            "without an expiry the account could never be sent another link");
    }

    [Test]
    public async Task AnUnreachableCache_AllowsTheResend()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        Assert.That(await Throttle(cache.Object).TryAcquireAsync("user-1"), Is.True);
    }
}
