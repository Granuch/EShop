using EShop.BuildingBlocks.Application.Behaviors;
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

        // Add pipeline behaviors
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
