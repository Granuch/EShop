using EShop.Basket.Application.Abstractions;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Configuration;
using EShop.Basket.Infrastructure.Consumers;
using EShop.Basket.Infrastructure.Idempotency;
using EShop.Basket.Infrastructure.Metrics;
using EShop.Basket.Infrastructure.Outbox;
using EShop.Basket.Infrastructure.Repositories;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Caching;
using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.BuildingBlocks.Infrastructure.HealthChecks;
using EShop.BuildingBlocks.Infrastructure.Services;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBasketInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();

        services.Configure<RedisBasketOptions>(configuration.GetSection(RedisBasketOptions.SectionName));

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisConnection = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Redis connection string is required.");

            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 5000;
            options.SyncTimeout = 5000;
            options.ConnectRetry = 3;
            options.KeepAlive = 60;
            options.ReconnectRetryPolicy = new LinearRetry(5000);

            return ConnectionMultiplexer.Connect(options);
        });

        services.AddScoped<IBasketRepository, RedisBasketRepository>();
        services.AddScoped<IIntegrationEventOutbox, BasketRedisOutbox>();
        services.AddSingleton<IBasketMetrics, BasketMetrics>();
        services.AddSingleton<ICheckoutIdempotencyStore, RedisCheckoutIdempotencyStore>();
        services.AddSingleton<RedisMessageIdempotencyStore>();

        services.AddScoped<ICacheInvalidationContext, CacheInvalidationContext>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

        return services;
    }

    public static IServiceCollection AddBasketMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        var settings = configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>();
        services.Configure<RabbitMqSettings>(configuration.GetSection(RabbitMqSettings.SectionName));

        if (settings == null || !settings.IsValid)
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    $"RabbitMQ configuration is invalid or missing in {RabbitMqSettings.SectionName} section.");
            }

            return services;
        }

        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<ProductPriceChangedConsumer>();
            bus.SetEndpointNameFormatter(new SnakeCaseEndpointNameFormatter(includeNamespace: false));

            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(settings.Host, (ushort)settings.Port, settings.VirtualHost, h =>
                {
                    h.Username(settings.Username);
                    h.Password(settings.Password);
                    h.PublisherConfirmation = true;
                    h.Heartbeat(TimeSpan.FromSeconds(settings.HeartbeatIntervalSeconds));

                    if (settings.UseSsl)
                    {
                        h.UseSsl(ssl =>
                        {
                            ssl.Protocol = System.Security.Authentication.SslProtocols.Tls12
                                           | System.Security.Authentication.SslProtocols.Tls13;
                        });
                    }

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

                cfg.Durable = true;
                cfg.PrefetchCount = settings.PrefetchCount;
                cfg.ConcurrentMessageLimit = settings.ConcurrencyLimit;

                if (settings.UseDelayedRedelivery
                    && settings.UseDelayedExchangePlugin
                    && settings.DelayedRedeliveryIntervalsMinutes.Length > 0)
                {
                    cfg.UseDelayedRedelivery(r =>
                    {
                        r.Intervals(settings.DelayedRedeliveryIntervalsMinutes
                            .Select(m => TimeSpan.FromMinutes(m))
                            .ToArray());

                        r.Ignore<ArgumentException>();
                        r.Ignore<FormatException>();
                        r.Ignore<NotSupportedException>();
                    });
                }

                cfg.UseMessageRetry(r =>
                {
                    r.Incremental(
                        settings.RetryCount,
                        TimeSpan.FromSeconds(settings.RetryIntervalSeconds),
                        TimeSpan.FromSeconds(settings.RetryIncrementSeconds));

                    r.Ignore<ArgumentException>();
                    r.Ignore<FormatException>();
                    r.Ignore<NotSupportedException>();
                });

                cfg.UseCircuitBreaker(cb =>
                {
                    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                    cb.TripThreshold = settings.CircuitBreakerThreshold;
                    cb.ActiveThreshold = settings.CircuitBreakerActiveThreshold;
                    cb.ResetInterval = TimeSpan.FromSeconds(settings.CircuitBreakerDurationSeconds);
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        services.Configure<MassTransitHostOptions>(options =>
        {
            options.WaitUntilStarted = settings.WaitUntilStarted;
            options.StartTimeout = TimeSpan.FromSeconds(Math.Max(5, settings.StartTimeoutSeconds));
            options.StopTimeout = TimeSpan.FromSeconds(30);
        });

        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["messaging", "ready"]);

        services.AddHostedService<BasketRedisOutboxProcessorService>();

        return services;
    }
}
