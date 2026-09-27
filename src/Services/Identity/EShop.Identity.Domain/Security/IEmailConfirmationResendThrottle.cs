namespace EShop.Identity.Domain.Security;

/// <summary>
/// Limits how often one account can be sent a fresh email-confirmation link through
/// <c>POST /api/v1/auth/resend-confirmation</c>.
///
/// <para>
/// The endpoint is anonymous and answers the same 200 for every address, so the per-IP
/// <c>login</c> rate limit is all that otherwise stands between a stranger and filling someone's
/// inbox from many addresses. This is the per-account half: one resend per cooldown, whoever asks.
/// </para>
///
/// <para>
/// Lives in Domain beside <see cref="ILoginAttemptTracker"/> so the Application handler can depend
/// on it while the distributed-cache implementation stays in Infrastructure.
/// </para>
/// </summary>
public interface IEmailConfirmationResendThrottle
{
    /// <summary>
    /// Claims the account's resend slot. Returns <c>true</c> when a resend may go out now (and
    /// starts the cooldown), <c>false</c> while an earlier resend's cooldown is still running.
    /// </summary>
    Task<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken = default);
}
