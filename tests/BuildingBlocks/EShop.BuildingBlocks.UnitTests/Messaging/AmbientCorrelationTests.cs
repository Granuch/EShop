using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Infrastructure.Services;
using Microsoft.AspNetCore.Http;

namespace EShop.BuildingBlocks.UnitTests.Messaging;

/// <summary>
/// M7 (Catalog audit Stage 7). <see cref="AmbientCorrelation"/> is what lets a domain-event handler
/// running in <c>OutboxProcessorService</c>'s background scope report the originating request's
/// correlation id instead of a freshly minted one. The end-to-end proof is Catalog's
/// <c>OutboxCorrelationTests</c>; these pin the scoping rules it depends on — above all that a value
/// is bounded by its scope, because the processor dispatches a whole batch in one DI scope and a
/// leaked value would stamp one message's id onto the next.
/// </summary>
[TestFixture]
public class AmbientCorrelationTests
{
    [Test]
    public void Begin_MakesTheIdCurrent_AndDisposingRestoresThePreviousValue()
    {
        Assert.That(AmbientCorrelation.Current, Is.Null);

        using (AmbientCorrelation.Begin("req-1"))
        {
            Assert.That(AmbientCorrelation.Current, Is.EqualTo("req-1"));
        }

        Assert.That(AmbientCorrelation.Current, Is.Null);
    }

    [Test]
    public void NestedScopes_RestoreTheOuterValue()
    {
        using (AmbientCorrelation.Begin("outer"))
        {
            using (AmbientCorrelation.Begin("inner"))
            {
                Assert.That(AmbientCorrelation.Current, Is.EqualTo("inner"));
            }

            Assert.That(AmbientCorrelation.Current, Is.EqualTo("outer"));
        }
    }

    /// <summary>
    /// An outbox row can have a null correlation id. Passing it must not wipe a value an enclosing
    /// operation set — callers hand over whatever they have without checking it first.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ABlankId_LeavesTheCurrentValueInPlace(string? blank)
    {
        using (AmbientCorrelation.Begin("outer"))
        {
            using (AmbientCorrelation.Begin(blank))
            {
                Assert.That(AmbientCorrelation.Current, Is.EqualTo("outer"));
            }

            Assert.That(AmbientCorrelation.Current, Is.EqualTo("outer"));
        }
    }

    /// <summary>
    /// The handler is reached through an awaited <c>mediator.Publish</c>, so the value has to flow
    /// across the await — the whole reason this is async-local rather than thread-static.
    /// </summary>
    [Test]
    public async Task TheValueFlowsIntoAwaitedCalls()
    {
        static async Task<string?> ReadAfterYieldAsync()
        {
            await Task.Yield();
            return AmbientCorrelation.Current;
        }

        using (AmbientCorrelation.Begin("req-2"))
        {
            Assert.That(await ReadAfterYieldAsync(), Is.EqualTo("req-2"));
        }
    }

    [Test]
    public async Task ConcurrentFlows_DoNotSeeEachOthersValue()
    {
        static async Task<string?> RunUnderAsync(string id)
        {
            using (AmbientCorrelation.Begin(id))
            {
                await Task.Delay(20);
                return AmbientCorrelation.Current;
            }
        }

        var results = await Task.WhenAll(RunUnderAsync("a"), RunUnderAsync("b"));

        Assert.That(results, Is.EqualTo(new[] { "a", "b" }));
    }

    /// <summary>
    /// The defect itself: outside HTTP, <see cref="HttpCurrentUserContext"/> minted a new id even
    /// when the id of the work being done was known.
    /// </summary>
    [Test]
    public void HttpCurrentUserContext_WithoutAnHttpContext_ReportsTheAmbientId()
    {
        var context = new HttpCurrentUserContext(new HttpContextAccessor());

        using (AmbientCorrelation.Begin("from-the-outbox-row"))
        {
            Assert.That(context.CorrelationId, Is.EqualTo("from-the-outbox-row"));
        }
    }

    /// <summary>
    /// The context is scoped and so is shared by every message in an outbox batch; it must read the
    /// ambient value on each access rather than capture it once.
    /// </summary>
    [Test]
    public void HttpCurrentUserContext_ReadsTheAmbientIdOnEveryAccess()
    {
        var context = new HttpCurrentUserContext(new HttpContextAccessor());

        using (AmbientCorrelation.Begin("first"))
        {
            Assert.That(context.CorrelationId, Is.EqualTo("first"));
        }

        using (AmbientCorrelation.Begin("second"))
        {
            Assert.That(context.CorrelationId, Is.EqualTo("second"));
        }
    }

    [Test]
    public void HttpCurrentUserContext_WithNothingAmbient_KeepsOneStableIdOfItsOwn()
    {
        var context = new HttpCurrentUserContext(new HttpContextAccessor());

        var first = context.CorrelationId;

        Assert.That(first, Is.Not.Null.And.Not.Empty);
        Assert.That(context.CorrelationId, Is.EqualTo(first));
    }

    [Test]
    public void SystemUserContext_ReportsTheAmbientId_AndFallsBackToItsOwn()
    {
        using (AmbientCorrelation.Begin("req-3"))
        {
            Assert.That(SystemUserContext.Instance.CorrelationId, Is.EqualTo("req-3"));
        }

        Assert.That(SystemUserContext.Instance.CorrelationId, Does.StartWith("system-"));
    }
}
