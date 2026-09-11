using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.HealthChecks;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.BuildingBlocks.Infrastructure.Caching;
using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Configuration;
using EShop.Ordering.Infrastructure.Consumers;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.Infrastructure.QueryServices;
using EShop.Ordering.Infrastructure.Repositories;
using EShop.Ordering.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EShop.Ordering.Infrastructure.Extensions;

/// <summary>
/// Extension methods for adding Ordering infrastructure services
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrderingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryDatabase = false,
        string? inMemoryDatabaseName = null)
    {
        // Add ICurrentUserContext for audit field population
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
        services.Configure<PaymentSuccessConsumer.PaymentSuccessProcessingOptions>(
            configuration.GetSection(PaymentSuccessConsumer.PaymentSuccessProcessingOptions.SectionName));

        // CachingBehavior only — it must be in Infrastructure for the IDistributedCache wiring, and
        // its position inside the transaction is immaterial because queries are not transactional.
        // CacheInvalidationBehavior deliberately does NOT belong here: registering it after
        // AddOrderingApplication puts it inside TransactionBehavior, so the keys AddOrderItem,
        // CancelOrder, RemoveOrderItem and ShipOrder add to ICacheInvalidationContext would be
        // drained before the write commits. It is registered by AddEShopCacheInvalidation() in
        // Program.cs instead — see that method for why.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));

        // Add DbContext
        if (useInMemoryDatabase)
        {
            var dbName = inMemoryDatabaseName ?? $"OrderingTestDb_{Guid.NewGuid()}";
            services.AddDbContext<OrderingDbContext>(options =>
                options.UseInMemoryDatabase(dbName));
        }
        else
        {
            services.AddDbContext<OrderingDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("OrderingDb")));
        }

        // Add repositories
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Add query services (keeps EF Core query composition in Infrastructure)
        services.AddScoped<IOrderQueryService, OrderQueryService>();

        // Catalog is where order lines get their name and price (audit C1). ValidateOnStart so a
        // deployment without CatalogService:BaseUrl fails to boot rather than failing every order.
        services.AddOptions<CatalogServiceOptions>()
            .Bind(configuration.GetSection(CatalogServiceOptions.SectionName))
            .Validate(
                o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                $"{CatalogServiceOptions.SectionName}:BaseUrl must be an absolute URI.")
            .ValidateOnStart();

        services.AddHttpClient<IProductCatalogReader, CatalogProductCatalogReader>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<CatalogServiceOptions>>().Value;

            // The reader requests a relative path; without a trailing slash the base's last segment
            // would be replaced rather than extended.
            client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        });

        // Register IUnitOfWork
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<OrderingDbContext>());

        // Register DbContext base type for OutboxProcessorService
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<OrderingDbContext>());

        // Register Outbox Processor background service
        services.AddSingleton(new OutboxProcessorOptions
        {
            BatchSize = 20,
            PollingIntervalMs = 1000,
            MaxRetries = 5,
            ErrorRetryDelayMs = 5000
        });
        services.AddHostedService<OutboxProcessorService>();

        // Register Outbox Cleanup background service
        services.AddSingleton(new OutboxCleanupOptions
        {
            RetentionDays = 7,
            CleanupIntervalHours = 6
        });
        services.AddHostedService<OutboxCleanupService>();

        // Register Outbox health check
        services.AddSingleton(new OutboxHealthCheckOptions
        {
            DeadLetterWarningThreshold = 10,
            PendingWarningThreshold = 100
        });
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck>(
                "outbox",
                tags: ["ready", "outbox"]);

        return services;
    }

    /// <summary>
    /// Adds MassTransit with RabbitMQ transport for the Ordering service.
    /// </summary>
    public static IServiceCollection AddOrderingMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.AddMessaging<OrderingDbContext>(
            configuration,
            "ordering",
            isDevelopment,
            bus =>
            {
                bus.AddConsumer<BasketCheckedOutConsumer>();
                bus.AddConsumer<PaymentSuccessConsumer>();
                bus.AddConsumer<PaymentFailedConsumer>();
            });

        return services;
    }
}
