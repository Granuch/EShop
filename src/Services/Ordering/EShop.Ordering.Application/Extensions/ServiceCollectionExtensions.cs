using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.Application.Extensions;

/// <summary>
/// Extension methods for adding Ordering application services
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrderingApplication(this IServiceCollection services)
    {
        var assembly = typeof(CreateOrderCommand).Assembly;

        // Add MediatR
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
        });

        // Add FluentValidation
        services.AddValidatorsFromAssembly(assembly);

        // Configure caching options for Ordering service
        services.Configure<CachingBehaviorOptions>(options =>
        {
            options.KeyPrefix = "ordering:";
            options.Version = "v1";
            options.UseVersioning = true;
            options.DefaultDuration = TimeSpan.FromMinutes(5);
        });

        // Add pipeline behaviors in correct order:
        // 1. Transaction (wraps everything in a transaction)
        // 2. Validation (validates before handler executes)
        // 3. Logging (logs request/response)
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
