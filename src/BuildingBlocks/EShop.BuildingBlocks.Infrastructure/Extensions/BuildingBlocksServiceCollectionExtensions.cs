using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.BuildingBlocks.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering Building Blocks services.
/// </summary>
public static class BuildingBlocksServiceCollectionExtensions
{
    /// <summary>
    /// Adds all building blocks infrastructure services.
    /// Call this in your service's Program.cs.
    /// </summary>
    public static IServiceCollection AddBuildingBlocksInfrastructure(
        this IServiceCollection services,
        Action<BuildingBlocksOptions>? configure = null)
    {
        var options = new BuildingBlocksOptions();
        configure?.Invoke(options);

        // Register ICurrentUserContext
        // HttpContextAccessor is required for HTTP-based user resolution
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();

        // Register MediatR pipeline behaviors in correct order
        // Order: Transaction -> Validation -> Logging -> Caching
        if (options.EnableTransactionBehavior)
        {
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        }

        if (options.EnableValidationBehavior)
        {
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        }

        if (options.EnableLoggingBehavior)
        {
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        }

        if (options.EnableCachingBehavior)
        {
            services.Configure<CachingBehaviorOptions>(opts =>
            {
                opts.DefaultDuration = options.DefaultCacheDuration;
                opts.KeyPrefix = options.CacheKeyPrefix;
                opts.Version = options.CacheVersion;
            });

            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
        }

        // Register Outbox processor if enabled
        if (options.EnableOutboxProcessor)
        {
            services.AddSingleton(new OutboxProcessorOptions
            {
                BatchSize = options.OutboxBatchSize,
                PollingIntervalMs = options.OutboxPollingIntervalMs,
                MaxRetries = options.OutboxMaxRetries,
                ErrorRetryDelayMs = options.OutboxErrorRetryDelayMs
            });

            services.AddHostedService<OutboxProcessorService>();
        }

        return services;
    }

    /// <summary>
    /// Registers only the ICurrentUserContext without other behaviors.
    /// Useful for services that don't need the full building blocks stack.
    /// </summary>
    public static IServiceCollection AddCurrentUserContext(this IServiceCollection services)
    {
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
        return services;
    }

    /// <summary>
    /// Registers the system user context for background services.
    /// Use this when running operations outside of HTTP request context.
    /// </summary>
    public static IServiceCollection AddSystemUserContext(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentUserContext>(SystemUserContext.Instance);
        return services;
    }
}

/// <summary>
/// Configuration options for building blocks services.
/// </summary>
public class BuildingBlocksOptions
{
    // Pipeline Behaviors
    public bool EnableTransactionBehavior { get; set; } = true;
    public bool EnableValidationBehavior { get; set; } = true;
    public bool EnableLoggingBehavior { get; set; } = true;
    public bool EnableCachingBehavior { get; set; } = true;

    // Caching options
    public TimeSpan DefaultCacheDuration { get; set; } = TimeSpan.FromMinutes(5);
    public string CacheKeyPrefix { get; set; } = "eshop:";
    public string CacheVersion { get; set; } = "v1";

    // Outbox processor options
    public bool EnableOutboxProcessor { get; set; } = true;
    public int OutboxBatchSize { get; set; } = 20;
    public int OutboxPollingIntervalMs { get; set; } = 1000;
    public int OutboxMaxRetries { get; set; } = 5;
    public int OutboxErrorRetryDelayMs { get; set; } = 5000;
}
