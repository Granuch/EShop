using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.BuildingBlocks.Infrastructure.HealthChecks;
using EShop.BuildingBlocks.Infrastructure.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.Infrastructure.Extensions;

/// <summary>
/// Extension methods for configuring MassTransit with RabbitMQ transport.
/// Provides production-grade defaults: durable queues, retry policies,
/// circuit breaker, dead-letter handling, TLS support, and snake_case endpoint naming.
/// </summary>
public static class MassTransitServiceCollectionExtensions
{
    /// <summary>
    /// Adds MassTransit with RabbitMQ transport.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type used for the integration event outbox.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="isDevelopment">Whether the environment is Development/Testing (relaxes validation).</param>
    /// <param name="configureConsumers">Action to register consumers on the bus.</param>
    public static IServiceCollection AddMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
        where TDbContext : DbContext
    {
        var settings = configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>();

        // Bind RabbitMqSettings for DI (used by health check and other consumers)
        services.Configure<RabbitMqSettings>(configuration.GetSection(RabbitMqSettings.SectionName));

        if (settings == null || !settings.IsValid)
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    $"RabbitMQ configuration is invalid or missing in {RabbitMqSettings.SectionName} section. " +
                    "Provide Host, Username, and Password. " +
                    "This is required in non-Development environments.");
            }

            // Register integration event outbox even without bus (events will queue until bus is available)
            services.AddScoped<IIntegrationEventOutbox>(sp =>
            {
                var dbContext = sp.GetRequiredService<TDbContext>();
                return new IntegrationEventOutbox(dbContext);
            });

            return services;
        }

        // R23: Warn if SSL is not enabled in non-development environments
        if (!isDevelopment && !settings.UseSsl)
        {
            // Log at startup — do not throw to allow gradual TLS migration
            System.Diagnostics.Debug.WriteLine(
                "[SECURITY WARNING] RabbitMQ UseSsl is disabled in a non-Development environment. " +
                "Enable TLS in production to prevent credential interception.");
        }

        services.AddMassTransit(bus =>
        {
            // Register consumers
            configureConsumers?.Invoke(bus);

            // Use snake_case endpoint naming convention
            bus.SetEndpointNameFormatter(new SnakeCaseEndpointNameFormatter(includeNamespace: false));

            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(settings.Host, (ushort)settings.Port, settings.VirtualHost, h =>
                {
                    h.Username(settings.Username);
                    h.Password(settings.Password);

                    // Enable publisher confirms for reliable publishing
                    h.PublisherConfirmation = true;

                    // Connection heartbeat for NAT/load balancer timeout detection
                    h.Heartbeat(TimeSpan.FromSeconds(settings.HeartbeatIntervalSeconds));

                    // TLS/SSL
                    if (settings.UseSsl)
                    {
                        h.UseSsl(ssl =>
                        {
                            ssl.Protocol = System.Security.Authentication.SslProtocols.Tls12
                                           | System.Security.Authentication.SslProtocols.Tls13;
                        });
                    }

                    // R1: Cluster-aware connections for RabbitMQ HA deployments
                    if (settings.ClusterNodes.Length > 0)
                    {
                        h.UseCluster(c =>
                        {
                            foreach (var node in settings.ClusterNodes)
                            {
                                c.Node(node);
                            }
                        });
                    }
                });

                // Durable exchanges and queues by default
                cfg.Durable = true;

                // Explicit prefetch count
                cfg.PrefetchCount = settings.PrefetchCount;

                // R12: Delayed redelivery — second-tier retries with longer intervals
                // Applied BEFORE UseMessageRetry so it catches messages that exhaust all immediate retries.
                // Enabled only when delayed redelivery is configured and the RabbitMQ delayed-exchange plugin is in use.
                if (settings.UseDelayedRedelivery
                    && settings.UseDelayedExchangePlugin
                    && settings.DelayedRedeliveryIntervalsMinutes.Length > 0)
                {
                    cfg.UseDelayedRedelivery(r =>
                    {
                        r.Intervals(settings.DelayedRedeliveryIntervalsMinutes
                            .Select(m => TimeSpan.FromMinutes(m))
                            .ToArray());

                        // Same non-transient exceptions as immediate retries
                        r.Ignore<ArgumentException>();
                        r.Ignore<FormatException>();
                        r.Ignore<NotSupportedException>();
                    });
                }

                // Global retry policy: incremental backoff (immediate, in-process)
                cfg.UseMessageRetry(r =>
                {
                    r.Incremental(
                        settings.RetryCount,
                        TimeSpan.FromSeconds(settings.RetryIntervalSeconds),
                        TimeSpan.FromSeconds(settings.RetryIncrementSeconds));

                    // Do not retry non-transient exceptions
                    r.Ignore<ArgumentException>();
                    r.Ignore<FormatException>();
                    r.Ignore<NotSupportedException>();
                });

                // Circuit breaker: prevent overwhelming a failing consumer
                cfg.UseCircuitBreaker(cb =>
                {
                    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                    // TripThreshold is a percentage (0-100): trip when this % of messages fail
                    cb.TripThreshold = settings.CircuitBreakerThreshold;
                    // ActiveThreshold is the minimum message count before the breaker can trip
                    cb.ActiveThreshold = settings.CircuitBreakerActiveThreshold;
                    cb.ResetInterval = TimeSpan.FromSeconds(settings.CircuitBreakerDurationSeconds);
                });

                // Concurrency limit (global default — per-endpoint overrides
                // can be set via bus.AddConsumer<T>(cfg => cfg.ConcurrentMessageLimit = N))
                cfg.ConcurrentMessageLimit = settings.ConcurrencyLimit;

                // Configure all endpoints from registered consumers
                cfg.ConfigureEndpoints(context);
            });
        });

        services.Configure<MassTransitHostOptions>(options =>
        {
            options.WaitUntilStarted = settings.WaitUntilStarted;
            options.StartTimeout = TimeSpan.FromSeconds(Math.Max(5, settings.StartTimeoutSeconds));
            options.StopTimeout = TimeSpan.FromSeconds(30);
        });

        // Register integration event outbox
        services.AddScoped<IIntegrationEventOutbox>(sp =>
        {
            var dbContext = sp.GetRequiredService<TDbContext>();
            return new IntegrationEventOutbox(dbContext);
        });

        // Register RabbitMQ health check
        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>(
                "rabbitmq",
                tags: ["messaging", "ready"]);

        return services;
    }
}
