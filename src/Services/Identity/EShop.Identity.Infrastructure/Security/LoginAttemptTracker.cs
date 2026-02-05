using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace EShop.Identity.Infrastructure.Security;

/// <summary>
/// Implementation of login attempt tracking using distributed cache.
/// Thread-safe and suitable for distributed/multi-instance deployments.
/// </summary>
public class LoginAttemptTracker : ILoginAttemptTracker
{
    private readonly IDistributedCache _cache;
    private readonly BruteForceProtectionSettings _settings;
    private readonly ILogger<LoginAttemptTracker> _logger;

    // Cache key patterns
    private const string AccountAttemptsKey = "account_attempts"; // Failed attempts per account
    private const string IpAttemptsKey = "ip_attempts"; // Failed attempts per IP
    private const string IpSetKey = "account_ips"; // Set of IPs per account
    private const string AccountLockKey = "account_lock"; // Temporary account locks
    private const string IpBlockKey = "ip_block"; // IP blocks

    public LoginAttemptTracker(
        IDistributedCache cache,
        IOptions<BruteForceProtectionSettings> settings,
        ILogger<LoginAttemptTracker> logger)
    {
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<LoginAttemptValidationResult> ValidateAttemptAsync(
        string identifier,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var hashedIdentifier = IdentifierHasher.HashShort(identifier);

        // 1. Check if IP is blocked
        var isIpBlocked = await IsIpBlockedAsync(ipAddress, cancellationToken);
        if (isIpBlocked)
        {
            _logger.LogWarning(
                "Login attempt from blocked IP. IP={IpAddress}, HashedIdentifier={HashedIdentifier}",
                ipAddress, hashedIdentifier);

            return LoginAttemptValidationResult.Blocked(
                BlockReason.IpBlocked,
                await GetIpAttemptCountAsync(ipAddress, cancellationToken),
                DateTime.UtcNow.AddMinutes(_settings.IpBlockDurationMinutes),
                "This IP address has been temporarily blocked due to excessive failed login attempts");
        }

        // 2. Check if account is temporarily locked
        var isAccountLocked = await IsAccountLockedAsync(hashedIdentifier, cancellationToken);
        if (isAccountLocked)
        {
            var lockExpiration = await GetAccountLockExpirationAsync(hashedIdentifier, cancellationToken);
            _logger.LogWarning(
                "Login attempt for temporarily locked account. HashedIdentifier={HashedIdentifier}, IP={IpAddress}",
                hashedIdentifier, ipAddress);

            return LoginAttemptValidationResult.Blocked(
                BlockReason.AccountLocked,
                await GetAccountAttemptCountAsync(hashedIdentifier, cancellationToken),
                lockExpiration,
                "This account has been temporarily locked due to repeated failed login attempts");
        }

        // 3. Get failed attempt count for account
        var failedAttempts = await GetAccountAttemptCountAsync(hashedIdentifier, cancellationToken);

        // 4. Check for distributed attack pattern (many IPs for same account)
        var distinctIps = await GetDistinctIpCountForAccountAsync(hashedIdentifier, cancellationToken);
        var isSuspicious = distinctIps >= _settings.MaxDistinctIpsPerAccount;

        if (isSuspicious)
        {
            _logger.LogWarning(
                "Distributed attack pattern detected. HashedIdentifier={HashedIdentifier}, DistinctIPs={DistinctIps}, CurrentIP={IpAddress}",
                hashedIdentifier, distinctIps, ipAddress);

            // Apply aggressive throttling for distributed attacks
            var aggressiveDelay = Math.Min(
                _settings.MaxThrottleDelaySeconds,
                _settings.ProgressiveThrottleBaseDelaySeconds * 4);

            return LoginAttemptValidationResult.Blocked(
                BlockReason.DistributedAttackDetected,
                failedAttempts,
                null,
                "Unusual login pattern detected. Please try again later or contact support",
                isSuspicious: true);
        }

        // 5. Apply progressive throttling if threshold exceeded
        if (failedAttempts >= _settings.MaxFailedAttemptsBeforeThrottle)
        {
            var throttleDelay = CalculateThrottleDelay(failedAttempts);

            _logger.LogInformation(
                "Progressive throttling applied. HashedIdentifier={HashedIdentifier}, IP={IpAddress}, FailedAttempts={FailedAttempts}, DelaySeconds={DelaySeconds}",
                hashedIdentifier, ipAddress, failedAttempts, throttleDelay);

            return LoginAttemptValidationResult.Throttled(
                throttleDelay,
                failedAttempts,
                $"Too many failed attempts. Please wait {throttleDelay} seconds before trying again");
        }

        // All checks passed
        return LoginAttemptValidationResult.Allowed();
    }

    public async Task RecordFailedAttemptAsync(
        string identifier,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var hashedIdentifier = IdentifierHasher.HashShort(identifier);

        // Increment account-level counter
        var accountAttempts = await IncrementCounterAsync(
            GetCacheKey(AccountAttemptsKey, hashedIdentifier),
            _settings.AttemptTrackingWindowMinutes,
            cancellationToken);

        // Increment IP-level counter
        var ipAttempts = await IncrementCounterAsync(
            GetCacheKey(IpAttemptsKey, ipAddress),
            _settings.AttemptTrackingWindowMinutes,
            cancellationToken);

        // Track IP for this account (for distributed attack detection)
        await AddIpToAccountSetAsync(hashedIdentifier, ipAddress, cancellationToken);

        _logger.LogInformation(
            "Failed login attempt recorded. HashedIdentifier={HashedIdentifier}, IP={IpAddress}, AccountAttempts={AccountAttempts}, IpAttempts={IpAttempts}",
            hashedIdentifier, ipAddress, accountAttempts, ipAttempts);

        // Apply account lockout if threshold exceeded
        if (accountAttempts >= _settings.MaxFailedAttemptsBeforeLockout)
        {
            await LockAccountAsync(hashedIdentifier, cancellationToken);

            _logger.LogWarning(
                "Account temporarily locked due to failed attempts. HashedIdentifier={HashedIdentifier}, Attempts={Attempts}",
                hashedIdentifier, accountAttempts);
        }

        // Apply IP block if threshold exceeded
        if (ipAttempts >= _settings.MaxFailedAttemptsPerIp)
        {
            await BlockIpAsync(ipAddress, cancellationToken);

            _logger.LogWarning(
                "IP address blocked due to failed attempts. IP={IpAddress}, Attempts={Attempts}",
                ipAddress, ipAttempts);
        }
    }

    public async Task RecordSuccessfulLoginAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        var hashedIdentifier = IdentifierHasher.HashShort(identifier);

        // Clear all tracking for successful login
        await ResetAccountAttemptsAsync(identifier, cancellationToken);

        _logger.LogInformation(
            "Successful login recorded. Attempt counters reset. HashedIdentifier={HashedIdentifier}",
            hashedIdentifier);
    }

    public async Task ResetAccountAttemptsAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        var hashedIdentifier = IdentifierHasher.HashShort(identifier);

        // Remove all tracking entries for this account
        await _cache.RemoveAsync(GetCacheKey(AccountAttemptsKey, hashedIdentifier), cancellationToken);
        await _cache.RemoveAsync(GetCacheKey(IpSetKey, hashedIdentifier), cancellationToken);
        await _cache.RemoveAsync(GetCacheKey(AccountLockKey, hashedIdentifier), cancellationToken);

        _logger.LogInformation(
            "Account attempt counters reset. HashedIdentifier={HashedIdentifier}",
            hashedIdentifier);
    }

    public async Task<int> GetFailedAttemptCountAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        var hashedIdentifier = IdentifierHasher.HashShort(identifier);
        return await GetAccountAttemptCountAsync(hashedIdentifier, cancellationToken);
    }

    #region Private Helper Methods

    private async Task<int> GetAccountAttemptCountAsync(string hashedIdentifier, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(AccountAttemptsKey, hashedIdentifier);
        var value = await _cache.GetStringAsync(key, cancellationToken);
        return int.TryParse(value, out var count) ? count : 0;
    }

    private async Task<int> GetIpAttemptCountAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(IpAttemptsKey, ipAddress);
        var value = await _cache.GetStringAsync(key, cancellationToken);
        return int.TryParse(value, out var count) ? count : 0;
    }

    private async Task<bool> IsAccountLockedAsync(string hashedIdentifier, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(AccountLockKey, hashedIdentifier);
        var value = await _cache.GetStringAsync(key, cancellationToken);
        return value != null;
    }

    private async Task<DateTime?> GetAccountLockExpirationAsync(string hashedIdentifier, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(AccountLockKey, hashedIdentifier);
        var value = await _cache.GetStringAsync(key, cancellationToken);
        return value != null && DateTime.TryParse(value, out var expiration) ? expiration : null;
    }

    private async Task<bool> IsIpBlockedAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(IpBlockKey, ipAddress);
        var value = await _cache.GetStringAsync(key, cancellationToken);
        return value != null;
    }

    private async Task<int> GetDistinctIpCountForAccountAsync(string hashedIdentifier, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(IpSetKey, hashedIdentifier);
        var json = await _cache.GetStringAsync(key, cancellationToken);

        if (string.IsNullOrEmpty(json))
        {
            return 0;
        }

        try
        {
            var ips = JsonSerializer.Deserialize<HashSet<string>>(json);
            return ips?.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> IncrementCounterAsync(string key, int expirationMinutes, CancellationToken cancellationToken)
    {
        var currentValue = await _cache.GetStringAsync(key, cancellationToken);
        var newValue = int.TryParse(currentValue, out var count) ? count + 1 : 1;

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(expirationMinutes)
        };

        await _cache.SetStringAsync(key, newValue.ToString(), options, cancellationToken);
        return newValue;
    }

    private async Task AddIpToAccountSetAsync(string hashedIdentifier, string ipAddress, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(IpSetKey, hashedIdentifier);
        var json = await _cache.GetStringAsync(key, cancellationToken);

        HashSet<string> ips;
        if (!string.IsNullOrEmpty(json))
        {
            ips = JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>();
        }
        else
        {
            ips = new HashSet<string>();
        }

        ips.Add(ipAddress);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_settings.AttemptTrackingWindowMinutes)
        };

        await _cache.SetStringAsync(key, JsonSerializer.Serialize(ips), options, cancellationToken);
    }

    private async Task LockAccountAsync(string hashedIdentifier, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(AccountLockKey, hashedIdentifier);
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.TemporaryLockoutMinutes);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_settings.TemporaryLockoutMinutes)
        };

        await _cache.SetStringAsync(key, expiresAt.ToString("O"), options, cancellationToken);
    }

    private async Task BlockIpAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(IpBlockKey, ipAddress);
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.IpBlockDurationMinutes);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_settings.IpBlockDurationMinutes)
        };

        await _cache.SetStringAsync(key, expiresAt.ToString("O"), options, cancellationToken);
    }

    /// <summary>
    /// Calculates progressive throttle delay using exponential backoff.
    /// Formula: BaseDelay * 2^(attempts - threshold)
    /// Capped at MaxThrottleDelaySeconds to prevent excessive delays.
    /// 
    /// Example with defaults (base=2s, threshold=3):
    /// - 3 attempts: 2s
    /// - 4 attempts: 4s
    /// - 5 attempts: 8s
    /// - 6 attempts: 16s
    /// - 7+ attempts: 30s (capped)
    /// </summary>
    private int CalculateThrottleDelay(int failedAttempts)
    {
        var attemptsOverThreshold = failedAttempts - _settings.MaxFailedAttemptsBeforeThrottle;
        var delay = _settings.ProgressiveThrottleBaseDelaySeconds * Math.Pow(2, attemptsOverThreshold);
        return Math.Min((int)delay, _settings.MaxThrottleDelaySeconds);
    }

    private string GetCacheKey(string keyType, string identifier)
    {
        return $"{_settings.CacheKeyPrefix}:{keyType}:{identifier}";
    }

    #endregion
}
