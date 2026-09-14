using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Services;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.HealthChecks;

public sealed class SmtpHealthCheck : IHealthCheck
{
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
        client.CheckCertificateRevocation = _smtpSettings.CheckCertificateRevocation;

        try
        {
            await client.ConnectAsync(
                _smtpSettings.Host,
                _smtpSettings.Port,
                _smtpSettings.EffectiveSecurity.ToSocketOptions(),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_smtpSettings.Username))
            {
                await client.AuthenticateAsync(_smtpSettings.Username, _smtpSettings.Password, cancellationToken);
            }

            await client.DisconnectAsync(true, cancellationToken);
            return HealthCheckResult.Healthy("SMTP server is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SMTP health check failed.", ex);
        }
    }
}
