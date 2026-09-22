using EShop.Basket.Application.Common;
using EShop.Basket.Application.Queries.Admin;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EShop.Basket.UnitTests.Application;

/// <summary>Admin panel S14: the cursor, the age syntax, the validators and the abandoned-basket cutoff.</summary>
[TestFixture]
public class BasketAdminQueryTests
{
    // ---------- cursor ----------

    [Test]
    public void ACursor_RoundTrips()
    {
        var position = new BasketScanPosition(18446744073709551615, 42, 0x0123456789abcdef);

        Assert.That(BasketScanCursor.TryParse(BasketScanCursor.Format(position), out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(position));
    }

    [Test]
    public void NoCursor_IsTheStartOfTheWalk()
    {
        Assert.That(BasketScanCursor.TryParse(null, out var position), Is.True);
        Assert.That(position, Is.EqualTo(BasketScanPosition.Start));
    }

    [TestCase("12")]
    [TestCase("12-3")]
    [TestCase("12-3-00ff")]
    [TestCase("12-3-0123456789abcdef-1")]
    [TestCase("-3-0123456789abcdef")]
    [TestCase("12--3-0123456789abcdef")]
    [TestCase("+12-3-0123456789abcdef")]
    [TestCase("18446744073709551616-3-0123456789abcdef")]
    [TestCase("12-3-012345678Gabcdef")]
    public void ACursorThisApiDidNotIssue_IsRefused(string value)
    {
        Assert.That(BasketScanCursor.TryParse(value, out _), Is.False);
    }

    // ---------- age ----------

    [TestCase("90m", 90 * 60)]
    [TestCase("24h", 24 * 3600)]
    [TestCase("3d", 3 * 86400)]
    [TestCase("30d", 30 * 86400)]
    [TestCase("720h", 30 * 86400)]
    public void AnAge_IsAWholeNumberAndAUnit(string value, int seconds)
    {
        Assert.That(BasketAge.TryParse(value, out var age), Is.True);
        Assert.That(age, Is.EqualTo(TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>
    /// "24" is refused rather than read the way <see cref="TimeSpan"/> would read it — as 24 days. The last case would
    /// overflow a TimeSpan and throw, rather than be refused, if the bound were checked after multiplying.
    /// </summary>
    [TestCase("")]
    [TestCase("24")]
    [TestCase("h")]
    [TestCase("0h")]
    [TestCase("-1h")]
    [TestCase("1.5h")]
    [TestCase("1w")]
    [TestCase("1H")]
    [TestCase("31d")]
    [TestCase("721h")]
    [TestCase("999999999d")]
    public void AnythingElse_IsRefused(string value)
    {
        Assert.That(BasketAge.TryParse(value, out _), Is.False);
    }

    // ---------- validators ----------

    [TestCase(null, true)]
    [TestCase(1, true)]
    [TestCase(100, true)]
    [TestCase(0, false)]
    [TestCase(101, false)]
    public void ThePageSize_IsBounded(int? pageSize, bool valid)
    {
        var result = new GetBasketsQueryValidator().Validate(new GetBasketsQuery { PageSize = pageSize });

        Assert.That(result.IsValid, Is.EqualTo(valid));
    }

    /// <summary>The shared rules are applied from each validator; this is the one that shows /abandoned calls them.</summary>
    [Test]
    public void TheAbandonedValidator_AppliesTheSharedPagingRules()
    {
        var validator = new GetAbandonedBasketsQueryValidator();

        Assert.That(validator.Validate(new GetAbandonedBasketsQuery { PageSize = 101 }).IsValid, Is.False);
        Assert.That(validator.Validate(new GetAbandonedBasketsQuery { Cursor = "nonsense" }).IsValid, Is.False);
        Assert.That(validator.Validate(new GetAbandonedBasketsQuery()).IsValid, Is.True, "omitted: 24h, first page");
    }

    [Test]
    public void AnUnusableAge_IsReportedAgainstOlderThan()
    {
        var result = new GetAbandonedBasketsQueryValidator().Validate(new GetAbandonedBasketsQuery { OlderThan = "24" });

        Assert.That(result.Errors.Select(e => e.PropertyName), Is.EquivalentTo(new[] { "OlderThan" }));
    }

    [TestCase(null, null, true)]
    [TestCase(0, 1, true)]
    [TestCase(0, 100, true)]
    [TestCase(-1, 20, false)]
    [TestCase(0, 0, false)]
    [TestCase(0, 101, false)]
    public void TheDeadLetterPage_IsBounded(int? offset, int? limit, bool valid)
    {
        var result = new GetOutboxDeadLettersQueryValidator().Validate(new GetOutboxDeadLettersQuery { Offset = offset, Limit = limit });

        Assert.That(result.IsValid, Is.EqualTo(valid));
    }

    // ---------- handlers ----------

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static Mock<IBasketAdminReader> Reader()
    {
        var reader = new Mock<IBasketAdminReader>();
        reader
            .Setup(r => r.ScanBasketsAsync(It.IsAny<BasketScanPosition>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredBasketPage([], Next: null));
        return reader;
    }

    [TestCase(null, 24 * 60)]
    [TestCase("90m", 90)]
    [TestCase("2d", 2 * 24 * 60)]
    public async Task TheCutoff_IsNowLessTheAge(string? olderThan, int minutes)
    {
        var reader = Reader();
        var handler = new GetAbandonedBasketsQueryHandler(
            reader.Object, new FixedClock(Now), NullLogger<GetAbandonedBasketsQueryHandler>.Instance);

        var result = await handler.Handle(new GetAbandonedBasketsQuery { OlderThan = olderThan }, CancellationToken.None);

        var cutoff = Now.UtcDateTime.AddMinutes(-minutes);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.ModifiedBefore, Is.EqualTo(cutoff));
        reader.Verify(r => r.ScanBasketsAsync(BasketScanPosition.Start, BasketScanQuery.DefaultPageSize, cutoff, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TheCartList_AppliesNoCutoff_AndResumesFromTheCursor()
    {
        var reader = Reader();
        var handler = new GetBasketsQueryHandler(reader.Object, NullLogger<GetBasketsQueryHandler>.Instance);
        var from = new BasketScanPosition(77, 3, 0xabc);

        await handler.Handle(new GetBasketsQuery { Cursor = BasketScanCursor.Format(from), PageSize = 5 }, CancellationToken.None);

        reader.Verify(r => r.ScanBasketsAsync(from, 5, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task APage_MapsReadableAndUnreadableBaskets_AndItsNextCursor()
    {
        var basket = ShoppingBasket.Rehydrate(
            "user-1", Now.UtcDateTime.AddDays(-1), Now.UtcDateTime,
            [new StoredBasketItem(Guid.NewGuid(), "Mug", 2.5m, 4)]);
        var next = new BasketScanPosition(9, 1, 0xdef);
        var reader = new Mock<IBasketAdminReader>();
        reader
            .Setup(r => r.ScanBasketsAsync(It.IsAny<BasketScanPosition>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredBasketPage([new StoredBasketEntry("user-1", basket), new StoredBasketEntry("user-2", null)], next));
        var handler = new GetBasketsQueryHandler(reader.Object, NullLogger<GetBasketsQueryHandler>.Instance);

        var page = (await handler.Handle(new GetBasketsQuery(), CancellationToken.None)).Value;

        Assert.That(page.NextCursor, Is.EqualTo(BasketScanCursor.Format(next)));
        Assert.That(page.Items[0].IsReadable, Is.True);
        Assert.That(page.Items[0].Lines, Is.EqualTo(1));
        Assert.That(page.Items[0].TotalItems, Is.EqualTo(4));
        Assert.That(page.Items[0].TotalPrice, Is.EqualTo(10m));
        Assert.That(page.Items[1].IsReadable, Is.False);
        Assert.That(page.Items[1].TotalPrice, Is.Null);
    }

    /// <summary>Redis down is a 503-mapped failure, as on every other Basket route — not an exception and not a 400.</summary>
    [Test]
    public async Task RedisFailing_IsTheOperationFailedError()
    {
        var reader = new Mock<IBasketAdminReader>();
        reader
            .Setup(r => r.ScanBasketsAsync(It.IsAny<BasketScanPosition>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down (test double)"));
        var deadLetters = new Mock<IOutboxDeadLetterReader>();
        deadLetters
            .Setup(r => r.ReadDeadLettersAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down (test double)"));

        var carts = await new GetBasketsQueryHandler(reader.Object, NullLogger<GetBasketsQueryHandler>.Instance)
            .Handle(new GetBasketsQuery(), CancellationToken.None);
        var abandoned = await new GetAbandonedBasketsQueryHandler(reader.Object, new FixedClock(Now), NullLogger<GetAbandonedBasketsQueryHandler>.Instance)
            .Handle(new GetAbandonedBasketsQuery(), CancellationToken.None);
        var letters = await new GetOutboxDeadLettersQueryHandler(deadLetters.Object, NullLogger<GetOutboxDeadLettersQueryHandler>.Instance)
            .Handle(new GetOutboxDeadLettersQuery(), CancellationToken.None);

        Assert.That(carts.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
        Assert.That(abandoned.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
        Assert.That(letters.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
