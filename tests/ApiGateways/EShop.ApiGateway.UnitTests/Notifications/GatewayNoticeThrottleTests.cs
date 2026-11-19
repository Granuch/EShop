using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Notifications;

/// <summary>
/// Pins the limits that keep a burst of notices from exhausting the mailbox they share with customer email
/// (2026-11-18: one email per 429, ~550 in ten minutes, and Gmail refused every confirmation for a day).
/// </summary>
[TestFixture]
public sealed class GatewayNoticeThrottleTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 22, 39, 0, TimeSpan.Zero);

    private static (GatewayNoticeThrottle Throttle, ManualTimeProvider Clock) Build(int windowSeconds = 900, int dailyCap = 50)
    {
        var clock = new ManualTimeProvider(Start);
        var throttle = new GatewayNoticeThrottle(
            clock,
            Options.Create(new GatewayOptions
            {
                EmailNotificationWindowSeconds = windowSeconds,
                MaxEmailNotificationsPerDay = dailyCap
            }),
            NullLogger<GatewayNoticeThrottle>.Instance);
        return (throttle, clock);
    }

    private static EmailNotificationContext Notice(string eventType = "RateLimitExceeded", string route = "catalog-products-read-route")
        => new(eventType, route, 429, "/api/v1/products", null, null, "corr", Start.UtcDateTime);

    [Test]
    public void TheFirstNotice_IsSent_WithNothingHeldBack()
    {
        var (throttle, _) = Build();

        var decision = throttle.Evaluate(Notice());

        Assert.That(decision, Is.EqualTo(NoticeDecision.Send(0)));
    }

    [Test]
    public void ARepeatInsideTheWindow_IsHeldBack()
    {
        var (throttle, clock) = Build();
        throttle.Evaluate(Notice());

        clock.Advance(TimeSpan.FromSeconds(899));
        var decision = throttle.Evaluate(Notice());

        Assert.That(decision, Is.EqualTo(NoticeDecision.Suppressed(NoticeDecision.WindowReason)));
    }

    [Test]
    public void TheFirstNoticeAfterTheWindow_IsSent_AndCountsWhatWasHeldBack()
    {
        var (throttle, clock) = Build();
        throttle.Evaluate(Notice());
        for (var i = 0; i < 7; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(60));
            throttle.Evaluate(Notice());
        }

        clock.Advance(TimeSpan.FromSeconds(900));
        var afterWindow = throttle.Evaluate(Notice());
        var next = throttle.Evaluate(Notice());

        Assert.That(afterWindow, Is.EqualTo(NoticeDecision.Send(7)));
        Assert.That(next, Is.EqualTo(NoticeDecision.Suppressed(NoticeDecision.WindowReason)), "the count restarts with the new window");
    }

    [Test]
    public void EachEventTypeAndRoute_HasItsOwnWindow()
    {
        var (throttle, _) = Build();
        throttle.Evaluate(Notice());

        var otherRoute = throttle.Evaluate(Notice(route: "catalog-categories-read-route"));
        var otherEvent = throttle.Evaluate(Notice(eventType: "DownstreamFailure"));
        var sameAgain = throttle.Evaluate(Notice());

        Assert.That(otherRoute.IsSent, Is.True);
        Assert.That(otherEvent.IsSent, Is.True);
        Assert.That(sameAgain.IsSent, Is.False);
    }

    /// <summary>The incident's shape: 642 rate-limit notices on two routes within ten minutes.</summary>
    [Test]
    public void TheNovember18Burst_SendsOneNoticePerRoute()
    {
        var (throttle, clock) = Build();
        var sent = 0;
        for (var i = 0; i < 642; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(900));
            var route = i % 5 == 0 ? "catalog-categories-read-route" : "catalog-products-read-route";
            if (throttle.Evaluate(Notice(route: route)).IsSent)
            {
                sent++;
            }
        }

        Assert.That(sent, Is.EqualTo(2));
    }

    [Test]
    public void TheDailyCap_HoldsBackEveryKind_UntilTheNextUtcDay()
    {
        var (throttle, clock) = Build(windowSeconds: 0, dailyCap: 3);
        var first = Enumerable.Range(0, 3).Select(i => throttle.Evaluate(Notice(route: $"route-{i}")).IsSent).ToArray();

        var overCap = throttle.Evaluate(Notice(route: "route-0"));
        var newKindOverCap = throttle.Evaluate(Notice(route: "route-new"));

        clock.Advance(TimeSpan.FromHours(2)); // 22:39 + 2 h = 00:39 UTC on the next day
        var nextDay = throttle.Evaluate(Notice(route: "route-0"));

        Assert.That(first, Is.EqualTo(new[] { true, true, true }));
        Assert.That(overCap, Is.EqualTo(NoticeDecision.Suppressed(NoticeDecision.DailyCapReason)));
        Assert.That(newKindOverCap, Is.EqualTo(NoticeDecision.Suppressed(NoticeDecision.DailyCapReason)));
        Assert.That(nextDay, Is.EqualTo(NoticeDecision.Send(1)), "the held-back count survives the cap and is reported");
    }

    [Test]
    public void AZeroWindow_AndAZeroCap_TurnTheLimitsOff()
    {
        var (throttle, _) = Build(windowSeconds: 0, dailyCap: 0);

        var decisions = Enumerable.Range(0, 100).Select(_ => throttle.Evaluate(Notice()).IsSent);

        Assert.That(decisions, Is.All.True);
    }

    [Test]
    public void TheShippedDefaults_AreAFifteenMinuteWindowAndFiftyADay()
    {
        var options = new GatewayOptions();

        Assert.That(options.EmailNotificationWindowSeconds, Is.EqualTo(900));
        Assert.That(options.MaxEmailNotificationsPerDay, Is.EqualTo(50));
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
