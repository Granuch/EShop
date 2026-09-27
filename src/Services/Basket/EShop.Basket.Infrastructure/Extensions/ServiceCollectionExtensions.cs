using EShop.Basket.Application.Abstractions;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Admin;
using EShop.Basket.Infrastructure.Checkout;
using EShop.Basket.Infrastructure.Configuration;
using EShop.Basket.Infrastructure.Consumers;
using EShop.Basket.Infrastructure.Idempotency;
using EShop.Basket.Infrastructure.Metrics;
using EShop.Basket.Infrastructure.Outbox;
using EShop.Basket.Infrastructure.Repositories;
using EShop.Basket.Infrastructure.Services;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Caching;
using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.BuildingBlocks.Infrastructure.Extensions;
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
        // Catalog is where every added item gets its name and price. ValidateOnStart so a deployment
        // without CatalogService:BaseUrl fails to boot rather than failing every add-to-basket with a
        // 400; the tracked appsettings.json deliberately carries no URL to fall back on (Basket audit
        // C1, D1 — its old localhost default pointed at Basket's own container).
        services.AddOptions<CatalogServiceOptions>()
            .Bind(configuration.GetSection(CatalogServiceOptions.SectionName))
            .Validate(
                o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                $"{CatalogServiceOptions.SectionName}:BaseUrl must be an absolute URI.")
            .ValidateOnStart();

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

            // Synchronous, once, on first resolve (Basket audit L8 — reviewed, kept). With AbortOnConnectFail=false it
            // returns after the first attempt (at most ConnectTimeout) whether or not Redis answered, and keeps
            // reconnecting in the background, so Redis being down makes requests answer 503 rather than the host fail.
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddScoped<IBasketRepository, RedisBasketRepository>();
        services.AddHttpClient<IProductCatalogReader, CatalogProductCatalogReader>();
        services.AddSingleton<IBasketMetrics, BasketMetrics>();
        // Checkout's lock, completed marker and atomic commit, which also writes the outbox entry. There is no
        // IIntegrationEventOutbox here: its synchronous Enqueue was a fire-and-forget push (Basket audit H1, S3).
        services.AddSingleton<IBasketCheckoutStore, RedisBasketCheckoutStore>();
        // The admin replay endpoint needs it whether or not messaging is configured (Basket audit S7, D7).
        services.AddSingleton<BasketOutboxDeadLetters>();
        // Admin panel S14: the admin panel's read side — every stored basket by SCAN (#78/#79), and the dead letters
        // themselves rather than only their count (#81), through the same instance the replay endpoint uses.
        services.AddScoped<IBasketAdminReader, RedisBasketAdminReader>();
        services.AddSingleton<IOutboxDeadLetterReader>(sp => sp.GetRequiredService<BasketOutboxDeadLetters>());
        // The abandoned-basket cutoff reads the clock through it. AddBasketMessaging also adds it, but only when a bus is
        // configured, and the admin read must work without one.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RedisMessageIdempotencyStore>();
        // Price sync's record of the newest price change per product, so an older event cannot undo it (Basket audit M10).
        services.AddSingleton<PriceChangeWatermark>();

        // No CachingBehavior: Basket reads its baskets straight from Redis (Basket audit S5, D5).
        return services;
    }

    public static IServiceCollection AddBasketMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        // Basket audit S11 (debt 5): the shared bus, the one every AddMessaging service gets — transport, retries, circuit
        // breaker, the "basket" queue prefix, host options and the RabbitMQ health check. Basket has no DbContext, so it
        // takes the bus without the EF outbox; its own outbox is the Redis one below. A hand-written copy of that block
        // stood here, identical option for option, and could only drift.
        var busConfigured = services.AddEShopBus(
            configuration,
            "basket",
            isDevelopment,
            bus => bus.AddConsumer<ProductPriceChangedConsumer>());

        if (!busConfigured)
        {
            services.AddHostedService<OutboxNotDrainedWarning>();
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<BasketOutboxOptions>();
        services.AddHostedService<BasketRedisOutboxProcessorService>();

        return services;
    }
}
