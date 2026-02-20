using EShop.BuildingBlocks.Infrastructure.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Sockets;

namespace EShop.BuildingBlocks.Infrastructure.HealthChecks;

/// <summary>
/// Health check that verifies RabbitMQ broker connectivity via TCP socket probe.
/// This complements MassTransit's built-in bus health check by detecting broker-level
/// connectivity issues (DNS resolution, firewall, broker down) without depending on
/// MassTransit's internal state.
/// </summary>
public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqSettings? _settings;
    private readonly ILogger<RabbitMqHealthCheck> _logger;

    public RabbitMqHealthCheck(
        IOptions<RabbitMqSettings> settings,
        ILogger<RabbitMqHealthCheck> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_settings == null || !_settings.IsValid)
        {
            return HealthCheckResult.Degraded(
                "RabbitMQ is not configured",
                data: new Dictionary<string, object> { ["configured"] = false });
        }

        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            await client.ConnectAsync(_settings.Host, _settings.Port, cts.Token);

            return HealthCheckResult.Healthy(
                "RabbitMQ broker is reachable",
                new Dictionary<string, object>
                {
                    ["host"] = _settings.Host,
                    ["port"] = _settings.Port,
                    ["ssl"] = _settings.UseSsl
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RabbitMQ health check failed. Host={Host}, Port={Port}",
                _settings.Host, _settings.Port);

            return HealthCheckResult.Unhealthy(
                "Cannot connect to RabbitMQ broker",
                ex,
                new Dictionary<string, object>
                {
                    ["host"] = _settings.Host,
                    ["port"] = _settings.Port,
                    ["error"] = ex.Message
                });
        }
    }
}
