using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Caching;
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

        public IEnumerable<string> CacheFamiliesToInvalidate { get; init; } = [];
    }

    [SetUp]
    public void SetUp() => _cache = new Mock<IDistributedCache>();

    private CacheInvalidationBehavior<InvalidatingCommand, Result<string>> Behavior(
        CachingBehaviorOptions options,
        ICacheInvalidationContext? context = null,
        ICacheKeyVersionProvider? versionProvider = null)
        => new(
            _cache.Object,
            NullLogger<CacheInvalidationBehavior<InvalidatingCommand, Result<string>>>.Instance,
            Options.Create(options),
            context,
            versionProvider);

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
    /// L33. With versioning off, <c>CachingBehavior</c> writes <c>{KeyPrefix}{key}</c>; the evictor
    /// used to remove <c>{KeyPrefix}{Version}:{key}</c> regardless, matching nothing. Both now build
    /// the key through <see cref="CachingBehaviorOptions.StorageKeyFor"/>.
    /// </summary>
    [Test]
    public async Task WithVersioningOff_RemovesTheUnversionedKeyCachingBehaviorWrote()
    {
        var options = new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v3", UseVersioning = false };
        var behavior = Behavior(options);

        await behavior.Handle(
            new InvalidatingCommand(["product:1"]),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        _cache.Verify(c => c.RemoveAsync("eshop:product:1", It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync("eshop:v3:product:1", It.IsAny<CancellationToken>()), Times.Never);
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
    /// C2. The behavior invalidates <b>after</b> the handler returns, which is what makes its
    /// registration position load-bearing: outside <c>TransactionBehavior</c> that means after the
    /// commit, inside it means before. Registered inside — as all four services did until Stage 2 —
    /// a concurrent read landing between the eviction and the commit repopulates the cache with
    /// pre-commit data for the full TTL.
    ///
    /// <para>
    /// This pins the ordering the shared <c>AddEShopCacheInvalidation()</c> registration relies on:
    /// nothing is evicted until the handler has completed.
    /// </para>
    /// </summary>
    [Test]
    public async Task DoesNotInvalidateUntilTheHandlerHasReturned()
    {
        var behavior = Behavior(new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v1" });
        var evictedBeforeHandlerReturned = false;

        await behavior.Handle(
            new InvalidatingCommand(["product:abc"]),
            _ =>
            {
                _cache.Verify(
                    c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                    Times.Never);
                evictedBeforeHandlerReturned = false;
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        Assert.That(evictedBeforeHandlerReturned, Is.False);
        _cache.Verify(c => c.RemoveAsync("eshop:v1:product:abc", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// C2's other half. A handler that <i>throws</i> never commits, so it must not invalidate
    /// either — the exception propagates through this behavior untouched and eviction is skipped.
    /// Contrast <see cref="InvalidatesEvenWhenTheHandlerReturnsFailure"/>: a <c>Result</c> failure
    /// still commits (TransactionBehavior's documented behaviour) and therefore still evicts.
    /// </summary>
    [Test]
    public void SkipsInvalidationEntirelyWhenTheHandlerThrows()
    {
        var behavior = Behavior(new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v1" });

        Assert.ThrowsAsync<InvalidOperationException>(async () => await behavior.Handle(
            new InvalidatingCommand(["product:abc"]),
            _ => throw new InvalidOperationException("handler blew up"),
            CancellationToken.None));

        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "nothing committed, so nothing may be evicted");
    }

    /// <summary>
    /// Stage 2 added family support so Catalog's DEBT-16 invalidation could stop bypassing this
    /// behavior. A declared family is bumped through <see cref="ICacheKeyVersionProvider"/>, not
    /// removed — the keys stop being addressed and lapse on their own TTL.
    /// </summary>
    [Test]
    public async Task BumpsDeclaredCacheFamilies()
    {
        var versionProvider = new Mock<ICacheKeyVersionProvider>();
        var behavior = Behavior(
            new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v1" },
            versionProvider: versionProvider.Object);

        await behavior.Handle(
            new InvalidatingCommand([]) { CacheFamiliesToInvalidate = ["products:list"] },
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        versionProvider.Verify(p => p.BumpVersionAsync("products:list", It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "a family bump deletes nothing — that is the point of the indirection");
    }

    /// <summary>
    /// Families added from the handler are drained too, which is how a key that depends on loaded
    /// domain data reaches this behavior at all.
    /// </summary>
    [Test]
    public async Task DrainsKeysAndFamiliesAddedThroughTheContext()
    {
        var context = new CacheInvalidationContext();
        var versionProvider = new Mock<ICacheKeyVersionProvider>();
        var behavior = Behavior(
            new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v1" },
            context,
            versionProvider.Object);

        await behavior.Handle(
            new InvalidatingCommand([]),
            _ =>
            {
                context.AddKey("products:category:42");
                context.AddFamily("products:list");
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        _cache.Verify(c => c.RemoveAsync("eshop:v1:products:category:42", It.IsAny<CancellationToken>()), Times.Once);
        versionProvider.Verify(p => p.BumpVersionAsync("products:list", It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(context.GetKeys(), Is.Empty, "the context is scoped and must be drained");
        Assert.That(context.GetFamilies(), Is.Empty);
    }

    /// <summary>
    /// A family declared by a service that registered no <see cref="ICacheKeyVersionProvider"/> is
    /// a no-op plus a warning — the same silent-failure posture as a wildcard key, and worth
    /// pinning for the same reason. Only Catalog registers a provider today.
    /// </summary>
    [Test]
    public async Task SilentlySkipsFamiliesWhenNoVersionProviderIsRegistered()
    {
        var behavior = Behavior(new CachingBehaviorOptions { KeyPrefix = "eshop:", Version = "v1" });

        var response = await behavior.Handle(
            new InvalidatingCommand([]) { CacheFamiliesToInvalidate = ["products:list"] },
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        Assert.That(response.IsSuccess, Is.True);
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
