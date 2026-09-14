namespace EShop.Basket.Infrastructure.Outbox;

/// <summary>
/// How Basket's Redis outbox retries (Basket audit S7, H6). A failed publish used to go straight back onto the pending
/// list, so a single message ran all five attempts within milliseconds and was dead-lettered by a broker blip shorter
/// than a second. Now each failure waits the next delay in <see cref="RetryDelays"/>.
/// </summary>
public sealed class BasketOutboxOptions
{
    /// <summary>About four and a half hours from the first failure to the last attempt.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(1)
    ];

    /// <summary>The wait before retry <c>n</c> (1-based) is <c>RetryDelays[n - 1]</c>.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = DefaultRetryDelays;

    /// <summary>
    /// A publish that has not completed by then counts as a failed attempt. Whether MassTransit's publish blocks or
    /// throws while the broker is unreachable (audit open question 5), a hung publish now ends in a retry.
    /// </summary>
    public TimeSpan PublishTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a claimed message is protected from the recovery sweep.</summary>
    public TimeSpan ProcessingLeaseTtl { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan IdlePollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan RecoveryInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The first attempt plus one per retry delay.</summary>
    public int MaxAttempts => RetryDelays.Count + 1;
}
