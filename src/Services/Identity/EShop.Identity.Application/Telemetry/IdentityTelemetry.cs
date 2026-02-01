using System.Diagnostics;

namespace EShop.Identity.Application.Telemetry;

/// <summary>
/// Telemetry facade for Identity service metrics
/// Delegates to the actual metrics implementation in the API layer
/// </summary>
public static class IdentityTelemetry
{
    private static IIdentityMetrics? _metrics;

    public static void Initialize(IIdentityMetrics metrics)
    {
        _metrics = metrics;
    }

    // Login metrics
    public static void RecordLoginSuccess() => _metrics?.RecordLoginSuccess();
    public static void RecordLoginFailure(string reason) => _metrics?.RecordLoginFailure(reason);
    public static void RecordLogin2FARequired() => _metrics?.RecordLogin2FARequired();
    public static IDisposable? MeasureLoginDuration() => _metrics?.MeasureLoginDuration();

    // Registration metrics
    public static void RecordRegistrationSuccess() => _metrics?.RecordRegistrationSuccess();
    public static void RecordRegistrationFailure(string reason) => _metrics?.RecordRegistrationFailure(reason);

    // Token metrics
    public static void RecordTokenRefreshSuccess() => _metrics?.RecordTokenRefreshSuccess();
    public static void RecordTokenRefreshFailure(string reason) => _metrics?.RecordTokenRefreshFailure(reason);
    public static void RecordTokenRevocation() => _metrics?.RecordTokenRevocation();
    public static IDisposable? MeasureTokenGeneration() => _metrics?.MeasureTokenGeneration();

    // Password metrics
    public static void RecordPasswordChange(bool success) => _metrics?.RecordPasswordChange(success);
    public static void RecordPasswordReset(bool success) => _metrics?.RecordPasswordReset(success);
    public static void RecordForgotPassword() => _metrics?.RecordForgotPassword();

    // 2FA metrics
    public static void Record2FAEnable(bool success) => _metrics?.Record2FAEnable(success);
    public static void Record2FAVerify(bool success) => _metrics?.Record2FAVerify(success);
    public static void Record2FADisable(bool success) => _metrics?.Record2FADisable(success);

    // Email metrics
    public static void RecordEmailConfirmation(bool success) => _metrics?.RecordEmailConfirmation(success);

    // Security metrics - Brute force protection
    public static void RecordThrottledAttempt(int delaySeconds) => _metrics?.RecordThrottledAttempt(delaySeconds);
    public static void RecordAccountLocked(string reason) => _metrics?.RecordAccountLocked(reason);
    public static void RecordIpBlocked(string reason) => _metrics?.RecordIpBlocked(reason);
    public static void RecordDistributedAttackDetected() => _metrics?.RecordDistributedAttackDetected();
    public static void UpdateActiveAccountLocks(int count) => _metrics?.UpdateActiveAccountLocks(count);
    public static void UpdateActiveIpBlocks(int count) => _metrics?.UpdateActiveIpBlocks(count);
}

/// <summary>
/// Interface for metrics implementation
/// </summary>
public interface IIdentityMetrics
{
    void RecordLoginSuccess();
    void RecordLoginFailure(string reason);
    void RecordLogin2FARequired();
    IDisposable MeasureLoginDuration();

    void RecordRegistrationSuccess();
    void RecordRegistrationFailure(string reason);

    void RecordTokenRefreshSuccess();
    void RecordTokenRefreshFailure(string reason);
    void RecordTokenRevocation();
    IDisposable MeasureTokenGeneration();

    void RecordPasswordChange(bool success);
    void RecordPasswordReset(bool success);
    void RecordForgotPassword();

    void Record2FAEnable(bool success);
    void Record2FAVerify(bool success);
    void Record2FADisable(bool success);

    void RecordEmailConfirmation(bool success);

    // Security metrics - Brute force protection
    void RecordThrottledAttempt(int delaySeconds);
    void RecordAccountLocked(string reason);
    void RecordIpBlocked(string reason);
    void RecordDistributedAttackDetected();
    void UpdateActiveAccountLocks(int count);
    void UpdateActiveIpBlocks(int count);
}
