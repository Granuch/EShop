using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Background service that periodically cleans up expired and revoked refresh tokens
/// to prevent database bloat and reduce attack surface.
/// </summary>
public class ExpiredTokenCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExpiredTokenCleanupService> _logger;
    private readonly TokenCleanupSettings _settings;

    public ExpiredTokenCleanupService(
        IServiceProvider serviceProvider,
        ILogger<ExpiredTokenCleanupService> logger,
        IOptions<TokenCleanupSettings> settings)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Expired Token Cleanup Service is disabled by configuration");
            return;
        }

        _logger.LogInformation(
            "Expired Token Cleanup Service is starting with interval: {Interval} hours, retention: {Retention} days",
            _settings.CleanupIntervalHours,
            _settings.RetentionDays);

        // Wait on startup to allow the application to fully initialize
        var initialDelay = TimeSpan.FromMinutes(_settings.InitialDelayMinutes);
        await Task.Delay(initialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var cleanupService = scope.ServiceProvider.GetRequiredService<ITokenCleanupService>();
                await cleanupService.CleanupExpiredTokensAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred during token cleanup. Will retry in {Interval} hours",
                    _settings.CleanupIntervalHours);
            }

            // Wait for the next cleanup cycle
            try
            {
                var cleanupInterval = TimeSpan.FromHours(_settings.CleanupIntervalHours);
                await Task.Delay(cleanupInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Service is stopping, exit gracefully
                _logger.LogInformation("Expired Token Cleanup Service is stopping");
                break;
            }
        }
    }
}
