using Prometheus;
using EShop.Identity.Application.Telemetry;

namespace EShop.Identity.API.Infrastructure.Metrics;

/// <summary>
/// Custom Prometheus metrics for Identity service
/// </summary>
public class IdentityMetrics : IIdentityMetrics
{
    private static readonly Counter LoginAttemptsTotal = Prometheus.Metrics.CreateCounter(
        "identity_login_attempts_total",
        "Total number of login attempts",
        new CounterConfiguration
        {
            LabelNames = ["status", "reason"]
        });

    private static readonly Counter RegistrationsTotal = Prometheus.Metrics.CreateCounter(
        "identity_registrations_total",
        "Total number of user registrations",
        new CounterConfiguration
        {
            LabelNames = ["status", "reason"]
        });

    private static readonly Counter TokenRefreshTotal = Prometheus.Metrics.CreateCounter(
        "identity_token_refresh_total",
        "Total number of token refresh operations",
        new CounterConfiguration
        {
            LabelNames = ["status", "reason"]
        });

    private static readonly Counter TokenRevocationsTotal = Prometheus.Metrics.CreateCounter(
        "identity_token_revocations_total",
        "Total number of token revocations");

    private static readonly Counter PasswordOperationsTotal = Prometheus.Metrics.CreateCounter(
        "identity_password_operations_total",
        "Total number of password operations",
        new CounterConfiguration
        {
            LabelNames = ["operation", "status"]
        });

    private static readonly Counter TwoFactorOperationsTotal = Prometheus.Metrics.CreateCounter(
        "identity_2fa_operations_total",
        "Total number of two-factor authentication operations",
        new CounterConfiguration
        {
            LabelNames = ["operation", "status"]
        });

    private static readonly Counter EmailOperationsTotal = Prometheus.Metrics.CreateCounter(
        "identity_email_operations_total",
        "Total number of email operations",
        new CounterConfiguration
        {
            LabelNames = ["operation", "status"]
        });

    private static readonly Histogram LoginDuration = Prometheus.Metrics.CreateHistogram(
        "identity_login_duration_seconds",
        "Duration of login operations in seconds",
        new HistogramConfiguration
        {
            Buckets = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5]
        });

    private static readonly Histogram TokenGenerationDuration = Prometheus.Metrics.CreateHistogram(
        "identity_token_generation_duration_seconds",
        "Duration of token generation in seconds",
        new HistogramConfiguration
        {
            Buckets = [0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25]
        });

    // Login metrics
    public void RecordLoginSuccess() =>
        LoginAttemptsTotal.WithLabels("success", "none").Inc();

    public void RecordLoginFailure(string reason) =>
        LoginAttemptsTotal.WithLabels("failure", reason).Inc();

    public void RecordLogin2FARequired() =>
        LoginAttemptsTotal.WithLabels("2fa_required", "none").Inc();

    public IDisposable MeasureLoginDuration() =>
        LoginDuration.NewTimer();

    // Registration metrics
    public void RecordRegistrationSuccess() =>
        RegistrationsTotal.WithLabels("success", "none").Inc();

    public void RecordRegistrationFailure(string reason) =>
        RegistrationsTotal.WithLabels("failure", reason).Inc();

    // Token metrics
    public void RecordTokenRefreshSuccess() =>
        TokenRefreshTotal.WithLabels("success", "none").Inc();

    public void RecordTokenRefreshFailure(string reason) =>
        TokenRefreshTotal.WithLabels("failure", reason).Inc();

    public void RecordTokenRevocation() =>
        TokenRevocationsTotal.Inc();

    public IDisposable MeasureTokenGeneration() =>
        TokenGenerationDuration.NewTimer();

    // Password metrics
    public void RecordPasswordChange(bool success) =>
        PasswordOperationsTotal.WithLabels("change", success ? "success" : "failure").Inc();

    public void RecordPasswordReset(bool success) =>
        PasswordOperationsTotal.WithLabels("reset", success ? "success" : "failure").Inc();

    public void RecordForgotPassword() =>
        PasswordOperationsTotal.WithLabels("forgot", "requested").Inc();

    // 2FA metrics
    public void Record2FAEnable(bool success) =>
        TwoFactorOperationsTotal.WithLabels("enable", success ? "success" : "failure").Inc();

    public void Record2FAVerify(bool success) =>
        TwoFactorOperationsTotal.WithLabels("verify", success ? "success" : "failure").Inc();

    public void Record2FADisable(bool success) =>
        TwoFactorOperationsTotal.WithLabels("disable", success ? "success" : "failure").Inc();

    // Email metrics
    public void RecordEmailConfirmation(bool success) =>
        EmailOperationsTotal.WithLabels("confirm", success ? "success" : "failure").Inc();
}
