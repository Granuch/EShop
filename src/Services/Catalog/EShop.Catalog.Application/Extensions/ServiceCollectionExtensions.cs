using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.Application.Extensions;

/// <summary>
/// Extension methods for adding Catalog application services
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogApplication(this IServiceCollection services)
    {
        var assembly = typeof(CreateProductCommand).Assembly;

        // Add MediatR
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
        });

        // Add FluentValidation
        services.AddValidatorsFromAssembly(assembly);

        // Configure caching options for Catalog service
        // Prefix will be "catalog:v1:" for all cache keys
        services.Configure<CachingBehaviorOptions>(options =>
        {
            options.KeyPrefix = "catalog:";
            options.Version = "v1";
            options.UseVersioning = true;
            options.DefaultDuration = TimeSpan.FromMinutes(5);
        });

        // Add pipeline behaviors in correct order:
        // 1. Transaction (wraps everything in a transaction)
        // 2. Validation (validates before handler executes)
        // 3. Logging (logs request/response)
        // Note: CachingBehavior and CacheInvalidationBehavior are registered in Infrastructure layer
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
