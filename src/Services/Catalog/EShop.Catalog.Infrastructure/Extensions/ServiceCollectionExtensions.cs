using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.HealthChecks;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.BuildingBlocks.Infrastructure.Caching;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Caching;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.Infrastructure.QueryServices;
using EShop.Catalog.Infrastructure.Repositories;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.Catalog.Infrastructure.Extensions;

/// <summary>
/// Extension methods for adding Catalog infrastructure services
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryDatabase = false,
        string? inMemoryDatabaseName = null)
    {
        // Add ICurrentUserContext for audit field population
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();

        // CachingBehavior only — it must be in Infrastructure for the IDistributedCache wiring, and
        // its position inside the transaction is immaterial because queries are not transactional.
        // CacheInvalidationBehavior deliberately does NOT belong here: registering it after
        // AddCatalogApplication puts it inside TransactionBehavior, so it would evict and bump
        // family versions before the write commits. It is registered by AddEShopCacheInvalidation()
        // in Program.cs instead — see that method for why.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));

        // Add DbContext
        if (useInMemoryDatabase)
        {
            var dbName = inMemoryDatabaseName ?? $"CatalogTestDb_{Guid.NewGuid()}";
            services.AddDbContext<CatalogDbContext>(options =>
                options.UseInMemoryDatabase(dbName));
        }
        else
        {
            services.AddDbContext<CatalogDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("CatalogDb")));
        }

        // Add repositories
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // Add query services (keeps EF Core query composition in Infrastructure)
        services.AddScoped<IProductQueryService, ProductQueryService>();

        // DEBT-16. Backs IVersionedCacheKey, which is how the products:list family is invalidated:
        // its keys embed every filter/sort/page parameter, so they cannot be enumerated and cannot
        // be deleted by exact key. Without this registration CachingBehavior silently falls back to
        // unversioned keys and list results go stale for their full TTL again.
        services.AddScoped<ICacheKeyVersionProvider, DistributedCacheKeyVersionProvider>();

        // Register IUnitOfWork (implemented by CatalogDbContext via BaseDbContext)
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CatalogDbContext>());

        // Register DbContext base type for OutboxProcessorService
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<CatalogDbContext>());

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

        // Register Outbox health check for dead-letter and depth monitoring
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
    /// Adds MassTransit with RabbitMQ transport for the Catalog service.
    /// </summary>
    public static IServiceCollection AddCatalogMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        // No consumers (D6, Catalog audit Stage 7). UserRegisteredConsumer was a log-and-return stub:
        // Catalog holds no user-scoped data, yet IdempotentConsumer still wrote a processed_messages
        // row for every registration. The bus itself stays — Catalog publishes through the outbox.
        services.AddMessaging<CatalogDbContext>(configuration, "catalog", isDevelopment);

        return services;
    }
}
