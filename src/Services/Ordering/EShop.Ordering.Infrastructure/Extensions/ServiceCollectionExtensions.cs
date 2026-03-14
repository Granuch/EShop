using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.HealthChecks;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Consumers;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.Infrastructure.QueryServices;
using EShop.Ordering.Infrastructure.Repositories;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Add caching behaviors (must be in Infrastructure due to IDistributedCache dependency)
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

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
