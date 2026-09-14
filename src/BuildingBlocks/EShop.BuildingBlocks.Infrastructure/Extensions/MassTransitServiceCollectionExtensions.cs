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
    /// <param name="serviceName">
    /// Prefixes every receive endpoint (queue) this service declares — see
    /// <see cref="CreateEndpointNameFormatter"/>. Required, and must differ between services.
    /// </param>
    /// <param name="isDevelopment">Whether the environment is Development/Testing (relaxes validation).</param>
    /// <param name="configureConsumers">Action to register consumers on the bus.</param>
    public static IServiceCollection AddMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        bool isDevelopment,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
        where TDbContext : DbContext
    {
        services.AddEShopBus(configuration, serviceName, isDevelopment, configureConsumers);

        // The integration event outbox, with a bus or without one: without one (Development/Testing with no
        // RabbitMQ), events queue in the database until a broker is configured.
        services.AddScoped<IIntegrationEventOutbox>(sp =>
        {
            var dbContext = sp.GetRequiredService<TDbContext>();
            return new IntegrationEventOutbox(dbContext);
        });

        return services;
    }

    /// <summary>
    /// The bus alone: everything <see cref="AddMessaging{TDbContext}"/> configures — RabbitMQ transport, TLS, retries,
    /// delayed redelivery, circuit breaker, per-service endpoint naming, host options and the RabbitMQ health check —
    /// except the EF integration event outbox.
    ///
    /// <para>For a service with no <c>DbContext</c>. Basket, which is Redis-only, kept a hand-written copy of this block
    /// for that reason; it matched option for option and could only drift (Basket audit S11, debt 5).</para>
    /// </summary>
    /// <returns>
    /// Whether a bus was configured. False when RabbitMQ is not configured and <paramref name="isDevelopment"/> is true;
    /// outside Development and Testing a missing configuration throws instead.
    /// </returns>
    public static bool AddEShopBus(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        bool isDevelopment,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        // Checked before the no-broker early return, so a missing name fails in every environment
        // rather than only where RabbitMQ is configured.
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

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

            return false;
        }

        // R23: Warn if SSL is not enabled in non-development environments.
        //
        // This used to go to System.Diagnostics.Debug.WriteLine, which is compiled out entirely in
        // Release — so the one warning that mattered could never fire in the only environments it
        // was written for. It goes to the logger now. Still a warning rather than a throw, to
        // allow gradual TLS migration.
        if (!isDevelopment && !settings.UseSsl)
        {
            Serilog.Log.Warning(
                "[SECURITY] RabbitMQ UseSsl is disabled in a non-Development environment. "
                + "Enable TLS in production to prevent credential interception.");
        }

        services.AddMassTransit(bus =>
        {
            // Register consumers
            configureConsumers?.Invoke(bus);

            bus.SetEndpointNameFormatter(CreateEndpointNameFormatter(serviceName));

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

        // Register RabbitMQ health check
        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>(
                "rabbitmq",
                tags: ["messaging", "ready"]);

        return true;
    }

    /// <summary>
    /// The receive-endpoint naming every service must use: snake_case consumer name, <b>prefixed
    /// with the service name</b>, e.g. <c>payment_order_created</c>.
    ///
    /// <para>
    /// The prefix is the point. Every service shares one broker vhost — it has to, because
    /// exchanges live in a vhost and publishers and consumers must see the same ones — so without it
    /// a queue is named by consumer class alone, and two services with a same-named consumer bind
    /// the <i>same</i> queue. RabbitMQ then load-balances that queue between them instead of giving
    /// each a copy: Payment's and Notification's <c>OrderCreatedConsumer</c> both bound
    /// <c>order_created</c>, and on RabbitMQ 3.13, 20 published messages reached Payment 10 times
    /// and Notification 10 times. Nothing fails and nothing logs; each order just silently gets a
    /// payment record <i>or</i> a notification.
    /// </para>
    ///
    /// <para>
    /// Applied by <see cref="AddEShopBus"/>, which every service's bus now goes through — Basket's included, since
    /// Basket audit S11.
    /// </para>
    /// </summary>
    public static IEndpointNameFormatter CreateEndpointNameFormatter(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        return new SnakeCaseEndpointNameFormatter(serviceName, includeNamespace: false);
    }
}
