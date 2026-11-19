using EShop.ApiGateway.Configuration;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Notifications;

/// <summary>
/// Decides whether an operational notice is emailed or only counted.
/// <para>Two limits, both from <see cref="GatewayOptions"/>. Per event type and route, at most one notice per
/// <see cref="GatewayOptions.EmailNotificationWindowSeconds"/>; the next one that goes out carries the number held back
/// in between. Across everything, at most <see cref="GatewayOptions.MaxEmailNotificationsPerDay"/> per UTC day.</para>
/// <para>Without it every 429 was one email. On 2026-11-18, the night the shop moved to k3s, a 10-minute burst of 642
/// catalog 429s sent ~550 emails through the same Gmail account the Notification service uses. Gmail then refused
/// every message from that account with <c>5.4.5 Daily user sending limit exceeded</c>, customer confirmations
/// included, for a day.</para>
/// <para>State is in memory, per gateway instance: a restart starts a fresh window and a fresh daily count. The held-back
/// count is reported only if another notice of the same kind follows; the metrics keep the full numbers.</para>
/// </summary>
public sealed class GatewayNoticeThrottle
{
    private readonly TimeProvider _timeProvider;
    private readonly GatewayOptions _options;
    private readonly ILogger<GatewayNoticeThrottle> _logger;
    private readonly Dictionary<(string EventType, string Route), KindState> _kinds = new();
    private readonly object _sync = new();

    private DateOnly _day;
    private int _sentToday;
    private bool _capLogged;

    public GatewayNoticeThrottle(
        TimeProvider timeProvider,
        IOptions<GatewayOptions> options,
        ILogger<GatewayNoticeThrottle> logger)
    {
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public NoticeDecision Evaluate(EmailNotificationContext notice)
    {
        var now = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var window = TimeSpan.FromSeconds(Math.Max(0, _options.EmailNotificationWindowSeconds));
        var cap = _options.MaxEmailNotificationsPerDay;

        lock (_sync)
        {
            if (today != _day)
            {
                _day = today;
                _sentToday = 0;
                _capLogged = false;
            }

            var key = (notice.EventType, notice.Route);
            if (!_kinds.TryGetValue(key, out var kind))
            {
                kind = new KindState();
                _kinds[key] = kind;
            }

            if (kind.LastSentAt is { } lastSent && now - lastSent < window)
            {
                kind.HeldBack++;
                return NoticeDecision.Suppressed(NoticeDecision.WindowReason);
            }

            if (cap > 0 && _sentToday >= cap)
            {
                kind.HeldBack++;
                if (!_capLogged)
                {
                    _capLogged = true;
                    _logger.LogWarning(
                        "Gateway notices reached the daily cap of {Cap}; no more are emailed until 00:00 UTC. First held back: {EventType} on {Route}",
                        cap,
                        notice.EventType,
                        notice.Route);
                }

                return NoticeDecision.Suppressed(NoticeDecision.DailyCapReason);
            }

            _sentToday++;
            var heldBack = kind.HeldBack;
            kind.HeldBack = 0;
            kind.LastSentAt = now;
            return NoticeDecision.Send(heldBack);
        }
    }

    private sealed class KindState
    {
        public DateTimeOffset? LastSentAt { get; set; }
        public int HeldBack { get; set; }
    }
}

/// <param name="IsSent">Whether the notice goes to the queue.</param>
/// <param name="SuppressedBefore">For a sent notice, how many of its kind were held back since the previous one.</param>
/// <param name="Reason">For a held-back notice, <see cref="WindowReason"/> or <see cref="DailyCapReason"/>.</param>
public sealed record NoticeDecision(bool IsSent, int SuppressedBefore, string? Reason)
{
    public const string WindowReason = "window";
    public const string DailyCapReason = "daily-cap";

    public static NoticeDecision Send(int suppressedBefore) => new(true, suppressedBefore, null);

    public static NoticeDecision Suppressed(string reason) => new(false, 0, reason);
}
