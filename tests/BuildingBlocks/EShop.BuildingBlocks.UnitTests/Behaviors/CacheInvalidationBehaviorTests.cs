using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.BuildingBlocks.UnitTests.Behaviors;

/// <summary>
/// TEST-03. Pins the two behaviours of <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/>
/// that make <c>ICacheInvalidatingCommand</c> look more capable than it is. Both fail silently in
/// production — they log success, or log a warning nobody reads — so a test is the only place they
/// are visible.
///
/// <para>
/// This is not hypothetical. Stage 7's plan called for marking Identity's role-membership commands
/// with the key <c>user_roles:{UserId}</c> and deleting the hand-written invalidation. That key is
/// owned by <c>CachedUserRolesService</c>, which reads and writes it unprefixed, so the rewritten
/// key would have matched nothing, removed nothing, reported success — and silently reopened
/// SEC-02 (revoked roles staying live in freshly minted tokens for up to five minutes). Only a
/// pre-existing regression test caught it.
/// </para>
/// </summary>
[TestFixture]
public class CacheInvalidationBehaviorTests
{
    private Mock<IDistributedCache> _cache = null!;

    private sealed record InvalidatingCommand(IReadOnlyList<string> Keys)
        : IRequest<Result<string>>, ICacheInvalidatingCommand
    {
        public IEnumerable<string> CacheKeysToInvalidate => Keys;
    }

    [SetUp]
    public void SetUp() => _cache = new Mock<IDistributedCache>();

    private CacheInvalidationBehavior<InvalidatingCommand, Result<string>> Behavior(
        CachingBehaviorOptions options)
        => new(
            _cache.Object,
            NullLogger<CacheInvalidationBehavior<InvalidatingCommand, Result<string>>>.Instance,
            Options.Create(options));

    /// <summary>
    /// The key a command declares is <b>not</b> the key that gets removed: the behavior rewrites it
    /// as <c>{KeyPrefix}{Version}:{key}</c>. So this can only ever evict entries that
    /// <c>CachingBehavior</c> itself wrote under the same options. Anything a service caches in its
    /// own namespace is unreachable from this marker, no matter how exactly the key string matches.
    /// </summary>
    [Test]
    public async Task RewritesTheDeclaredKeyWithPrefixAndVersion()
    {
        var options = new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v3" };
        var behavior = Behavior(options);

        await behavior.Handle(
            new InvalidatingCommand(["user_roles:abc"]),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        _cache.Verify(c => c.RemoveAsync("eshop:v3:user_roles:abc", It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync("user_roles:abc", It.IsAny<CancellationToken>()), Times.Never,
            "the raw key a service owns is never what this behavior removes — that is the SEC-02 trap");
    }

    /// <summary>
    /// A wildcard key is accepted, removes nothing, and only logs a warning — IDistributedCache has
    /// no SCAN. A declared pattern therefore reads as working invalidation while being a no-op.
    /// </summary>
    [Test]
    public async Task SilentlySkipsWildcardKeys()
    {
        var behavior = Behavior(new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v1" });

        await behavior.Handle(
            new InvalidatingCommand(["products:list:*"]),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "pattern keys are logged as a warning and otherwise ignored");
    }

    /// <summary>
    /// Invalidation runs after the handler and is not conditional on the outcome — a command that
    /// fails still evicts. Worth pinning: it means a failed write cannot leave a stale entry, but
    /// it also means a no-op command pays the eviction.
    /// </summary>
    [Test]
    public async Task InvalidatesEvenWhenTheHandlerReturnsFailure()
    {
        var behavior = Behavior(new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v1" });

        await behavior.Handle(
            new InvalidatingCommand(["profile:abc"]),
            _ => Task.FromResult(Result<string>.Failure(new Error("Nope", "rejected"))),
            CancellationToken.None);

        _cache.Verify(c => c.RemoveAsync("eshop:v1:profile:abc", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A cache outage must not turn a successful command into a failed request.
    /// </summary>
    [Test]
    public async Task SwallowsCacheFailuresRatherThanFailingTheRequest()
    {
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis is down"));

        var behavior = Behavior(new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v1" });

        var response = await behavior.Handle(
            new InvalidatingCommand(["profile:abc"]),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        Assert.That(response.IsSuccess, Is.True);
    }
}
