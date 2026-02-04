using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Identity.Application.Auth.Commands.Register;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.Application.Extensions;

/// <summary>
/// Extension methods for adding application services
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        var assembly = typeof(RegisterCommand).Assembly;

        // Add MediatR
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
        });

        // Add FluentValidation
        services.AddValidatorsFromAssembly(assembly);

        // Configure caching options
        services.Configure<CachingBehaviorOptions>(options =>
        {
            options.KeyPrefix = "identity:";
            options.DefaultDuration = TimeSpan.FromMinutes(5);
        });

        // Add pipeline behaviors in correct order:
        // 1. Transaction (wraps everything in a transaction)
        // 2. Validation (validates before handler executes)
        // 3. Logging (logs request/response)
        // Note: CachingBehavior is registered in Infrastructure layer
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
