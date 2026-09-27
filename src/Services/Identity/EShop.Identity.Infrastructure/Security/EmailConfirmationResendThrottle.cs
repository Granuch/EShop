using EShop.Identity.Domain.Security;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Infrastructure.Security;

/// <summary>
/// <see cref="IEmailConfirmationResendThrottle"/> over <see cref="IDistributedCache"/>: one key per
/// account whose presence is the cooldown, expiring on its own.
///
/// <para>
/// <b>Read-then-write, not atomic.</b> Two resends for the same account racing inside a few
/// milliseconds can both pass and send two emails. That is accepted: the cost is one extra email,
/// the per-IP <c>login</c> rate limit still bounds a burst, and an atomic claim would need the raw
/// Redis connection, which Development does not have.
/// </para>
///
/// <para>
/// <b>Fails open.</b> If the cache is unreachable the resend is allowed and a warning logged, because
/// refusing would leave a user who never got their link unable to ask again for as long as Redis is
/// down — worse than an unthrottled resend behind the IP limit.
/// </para>
/// </summary>
public sealed class EmailConfirmationResendThrottle : IEmailConfirmationResendThrottle
{
    /// <summary>One resend per account per minute.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    private const string KeyPrefix = "email_confirmation_resend:";

    private readonly IDistributedCache _cache;
    private readonly ILogger<EmailConfirmationResendThrottle> _logger;

    public EmailConfirmationResendThrottle(IDistributedCache cache, ILogger<EmailConfirmationResendThrottle> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var key = KeyPrefix + userId;

        try
        {
            if (await _cache.GetAsync(key, cancellationToken) is not null)
            {
                return false;
            }

            await _cache.SetAsync(
                key,
                [1],
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Cooldown },
                cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Email-confirmation resend throttle unavailable; allowing the resend. UserId={UserId}", userId);
            return true;
        }
    }
}
