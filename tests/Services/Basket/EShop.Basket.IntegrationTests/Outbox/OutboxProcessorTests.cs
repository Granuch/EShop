using System.Text.Json;
using EShop.Basket.Application.Abstractions;
using EShop.Basket.Infrastructure.Outbox;
using EShop.Basket.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Messaging.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Outbox;

/// <summary>
/// Basket audit S7 (H6, L7). The first tests <see cref="BasketRedisOutboxProcessorService"/> has had: its Lua steps
/// against real Redis, one at a time, with a manual clock and a mocked publish endpoint. The mock stands in only for
/// the broker; every list, set and lease is real.
/// </summary>
[TestFixture]
[Category("Integration")]
public class OutboxProcessorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private BasketApiFactory _factory = null!;
    private ServiceProvider _services = null!;
    private IDatabase _database = null!;
    private ManualClock _clock = null!;
    private Mock<IPublishEndpoint> _publish = null!;
    private List<(object Message, IPipe<PublishContext> Pipe)> _published = null!;
    private BasketOutboxOptions _options = null!;
    private BasketRedisOutboxProcessorService _processor = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new BasketApiFactory();
        _database = _factory.Redis.GetDatabase();
        _clock = new ManualClock(Start);
        _published = [];

        _publish = new Mock<IPublishEndpoint>();
        PublishSucceeds();

        _services = new ServiceCollection().AddScoped(_ => _publish.Object).BuildServiceProvider();
        _options = new BasketOutboxOptions
        {
            RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)],
            PublishTimeout = TimeSpan.FromMilliseconds(300)
        };

        _processor = new BasketRedisOutboxProcessorService(
            _factory.Redis,
            _services.GetRequiredService<IServiceScopeFactory>(),
            _options,
            _clock,
            NullLogger<BasketRedisOutboxProcessorService>.Instance,
            Mock.Of<IBasketMetrics>());
    }

    [TearDown]
    public void TearDown()
    {
        _processor.Dispose();
        _services.Dispose();
        _factory.Dispose();
    }

    private void PublishSucceeds()
        => _publish
            .Setup(x => x.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<IPipe<PublishContext>>(), It.IsAny<CancellationToken>()))
            .Callback<object, Type, IPipe<PublishContext>, CancellationToken>((message, _, pipe, _) => _published.Add((message, pipe)))
            .Returns(Task.CompletedTask);

    private void PublishFails()
        => _publish
            .Setup(x => x.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<IPipe<PublishContext>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unreachable (test double)"));

    private static string Envelope(Guid id, int retryCount = 0)
    {
        var message = RedisOutboxMessage.Parse(RedisOutboxMessage.Serialize(
            new BasketCheckedOutEvent
            {
                EventId = id,
                UserId = "user-1",
                Items = [new CheckoutItem { ProductId = Guid.NewGuid(), ProductName = "Mug", Price = 10m, Quantity = 1 }],
                TotalPrice = 10m
            },
            correlationId: Guid.NewGuid().ToString()))!;

        return (message with { RetryCount = retryCount }).ToJson();
    }

    private async Task<RedisOutboxMessage> OnlyEntryAsync(string key)
    {
        (await _database.ListLengthAsync(key)).Should().Be(1, $"{key} should hold exactly one entry");
        return RedisOutboxMessage.Parse((await _database.ListGetByIndexAsync(key, 0)).ToString())!;
    }

    private static async Task<Guid?> MessageIdSetBy(IPipe<PublishContext> pipe)
    {
        var context = new Mock<PublishContext>();
        context.SetupAllProperties();
        await pipe.Send(context.Object);
        return context.Object.MessageId;
    }

    [Test]
    public async Task APendingMessage_IsPublishedUnderItsId_AndLeavesNothingBehind()
    {
        var id = Guid.NewGuid();
        await _database.ListLeftPushAsync(BasketOutboxKeys.Pending, Envelope(id));

        (await _processor.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();

        _published.Should().ContainSingle();
        ((BasketCheckedOutEvent)_published[0].Message).EventId.Should().Be(id);
        (await MessageIdSetBy(_published[0].Pipe)).Should().Be(id, "Ordering deduplicates on the MessageId");
        (await _database.ListLengthAsync(BasketOutboxKeys.Pending)).Should().Be(0);
        (await _database.ListLengthAsync(BasketOutboxKeys.Processing)).Should().Be(0);
        (await _database.KeyExistsAsync(BasketOutboxKeys.Lease(id))).Should().BeFalse();
    }

    [Test]
    public async Task AFailedPublish_WaitsForItsDelay_InsteadOfBeingRetriedStraightAway()
    {
        var id = Guid.NewGuid();
        await _database.ListLeftPushAsync(BasketOutboxKeys.Pending, Envelope(id));
        PublishFails();

        (await _processor.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();

        (await _database.ListLengthAsync(BasketOutboxKeys.Pending)).Should().Be(0,
            "H6: it used to go straight back to pending and use up every attempt within milliseconds");
        var scheduled = await _database.SortedSetRangeByScoreWithScoresAsync(BasketOutboxKeys.Retry);
        scheduled.Should().ContainSingle();
        scheduled[0].Score.Should().Be((Start + TimeSpan.FromSeconds(5)).ToUnixTimeMilliseconds());
        (await _database.KeyExistsAsync(BasketOutboxKeys.Lease(id))).Should().BeFalse();

        (await _processor.PromoteDueRetriesAsync()).Should().Be(0, "not due yet");
        (await _processor.ProcessNextAsync(CancellationToken.None)).Should().BeFalse();

        _clock.Advance(TimeSpan.FromSeconds(5));
        (await _processor.PromoteDueRetriesAsync()).Should().Be(1);
        (await OnlyEntryAsync(BasketOutboxKeys.Pending)).RetryCount.Should().Be(1);

        PublishSucceeds();
        (await _processor.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();
        _published.Should().ContainSingle();
        (await _database.SortedSetLengthAsync(BasketOutboxKeys.Retry)).Should().Be(0);
    }

    [Test]
    public async Task AMessageFailingItsLastAttempt_IsDeadLettered()
    {
        var id = Guid.NewGuid();
        await _database.ListLeftPushAsync(BasketOutboxKeys.Pending, Envelope(id, retryCount: _options.MaxAttempts - 1));
        PublishFails();

        await _processor.ProcessNextAsync(CancellationToken.None);

        (await OnlyEntryAsync(BasketOutboxKeys.DeadLetter)).RetryCount.Should().Be(_options.MaxAttempts);
        (await _database.SortedSetLengthAsync(BasketOutboxKeys.Retry)).Should().Be(0);
        (await _database.ListLengthAsync(BasketOutboxKeys.Processing)).Should().Be(0);
    }

    [Test]
    public async Task APublishCancelledByShutdown_GoesBackUnchanged_WithoutCountingAnAttempt()
    {
        var id = Guid.NewGuid();
        var envelope = Envelope(id);
        await _database.ListLeftPushAsync(BasketOutboxKeys.Pending, envelope);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        _publish
            .Setup(x => x.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<IPipe<PublishContext>>(), It.IsAny<CancellationToken>()))
            .Returns((object _, Type _, IPipe<PublishContext> _, CancellationToken token) => Task.FromCanceled(token));

        await FluentActions.Awaiting(() => _processor.ProcessNextAsync(stopping.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        (await OnlyEntryAsync(BasketOutboxKeys.Pending)).RetryCount.Should().Be(0);
        (await _database.ListGetByIndexAsync(BasketOutboxKeys.Pending, 0)).ToString().Should().Be(envelope);
        (await _database.SortedSetLengthAsync(BasketOutboxKeys.Retry)).Should().Be(0);
        (await _database.ListLengthAsync(BasketOutboxKeys.Processing)).Should().Be(0);
    }

    [Test]
    public async Task APublishThatHangs_TimesOut_AndCountsAsAFailedAttempt()
    {
        await _database.ListLeftPushAsync(BasketOutboxKeys.Pending, Envelope(Guid.NewGuid()));
        _publish
            .Setup(x => x.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<IPipe<PublishContext>>(), It.IsAny<CancellationToken>()))
            .Returns((object _, Type _, IPipe<PublishContext> _, CancellationToken token) => Task.Delay(Timeout.Infinite, token));

        // Bounded, not awaited directly: with the timeout gone this call never returns, and a plain await would hang the
        // whole run instead of failing this test.
        var processing = _processor.ProcessNextAsync(CancellationToken.None);
        var finished = await Task.WhenAny(processing, Task.Delay(TimeSpan.FromSeconds(10)));
        finished.Should().BeSameAs(processing, "the publish timeout must end a hung publish");
        (await processing).Should().BeTrue();

        (await _database.SortedSetLengthAsync(BasketOutboxKeys.Retry)).Should().Be(1,
            "open question 5: whether a publish blocks or throws while the broker is down, it now ends in a retry");
        (await _database.ListLengthAsync(BasketOutboxKeys.Processing)).Should().Be(0);
    }

    [Test]
    public async Task AMessageLeftByADeadProcessor_IsRecovered_WhileOneStillLeasedIsLeftAlone()
    {
        var abandoned = Guid.NewGuid();
        var inFlight = Guid.NewGuid();
        await _database.ListLeftPushAsync(BasketOutboxKeys.Processing, Envelope(abandoned));
        await _database.ListLeftPushAsync(BasketOutboxKeys.Processing, Envelope(inFlight));
        await _database.StringSetAsync(BasketOutboxKeys.Lease(inFlight), "busy", TimeSpan.FromMinutes(2));

        await _processor.RecoverStaleProcessingMessagesAsync(CancellationToken.None);

        (await OnlyEntryAsync(BasketOutboxKeys.Pending)).Id.Should().Be(abandoned);
        (await OnlyEntryAsync(BasketOutboxKeys.Processing)).Id.Should().Be(inFlight);
    }

    [Test]
    public async Task ClaimingAMessage_SetsItsLeaseInTheSameStep()
    {
        var id = Guid.NewGuid();
        await _database.ListLeftPushAsync(BasketOutboxKeys.Pending, Envelope(id));
        bool? leasedWhilePublishing = null;
        _publish
            .Setup(x => x.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<IPipe<PublishContext>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                leasedWhilePublishing = await _database.KeyExistsAsync(BasketOutboxKeys.Lease(id));
            });

        await _processor.ProcessNextAsync(CancellationToken.None);

        leasedWhilePublishing.Should().BeTrue("a claimed message without a lease is fair game for another instance's recovery");
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
