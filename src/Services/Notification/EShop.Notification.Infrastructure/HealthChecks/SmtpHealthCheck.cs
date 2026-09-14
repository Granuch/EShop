using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Services;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.HealthChecks;

/// <summary>
/// Whether the SMTP server accepts a connection: connect with the configured security, EHLO, disconnect.
/// <para>Notification audit S6 (M11, D9). It used to log in as well, on every readiness probe: every 10 seconds in k8s
/// and 30 in compose, about 8,600 logins a day per pod against a real provider, which invites throttling or a lockout.
/// It also gated readiness, which for a service that takes no inbound traffic gates nothing, while an SMTP outage is
/// already recorded per message and retried. It is now reported on <c>/health</c> only, and never authenticates.</para>
/// </summary>
public sealed class SmtpHealthCheck : IHealthCheck
{
    /// <summary>MailKit allows two minutes per operation; a health check must answer long before that.</summary>
    public const int TimeoutMilliseconds = 5000;

    private readonly SmtpSettings _smtpSettings;

    public SmtpHealthCheck(IOptions<SmtpSettings> smtpSettings)
    {
        _smtpSettings = smtpSettings.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_smtpSettings.Host))
        {
            return HealthCheckResult.Unhealthy("SMTP host is not configured.");
        }

        using var client = new SmtpClient();
        client.Timeout = TimeoutMilliseconds;
        client.CheckCertificateRevocation = _smtpSettings.CheckCertificateRevocation;

        try
        {
            await client.ConnectAsync(
                _smtpSettings.Host,
                _smtpSettings.Port,
                _smtpSettings.EffectiveSecurity.ToSocketOptions(),
                cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return HealthCheckResult.Healthy("SMTP server accepts connections.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SMTP server does not accept connections.", ex);
        }
    }
}
