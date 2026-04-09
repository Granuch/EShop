using EShop.ApiGateway.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Health;

public sealed class SmtpGatewayHealthCheck : IHealthCheck
{
    private readonly EmailOptions _options;

    public SmtpGatewayHealthCheck(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            return HealthCheckResult.Degraded("SMTP host is not configured.");
        }

        using var client = new SmtpClient();
        client.CheckCertificateRevocation = _options.CheckCertificateRevocation;

        try
        {
            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                _options.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            }

            await client.DisconnectAsync(true, cancellationToken);
            return HealthCheckResult.Healthy("SMTP is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SMTP health check failed.", ex);
        }
    }
}
