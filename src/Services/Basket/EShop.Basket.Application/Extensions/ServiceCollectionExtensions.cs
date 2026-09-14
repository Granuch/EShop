using EShop.Basket.Application.Commands.AddItemToBasket;
using EShop.BuildingBlocks.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBasketApplication(this IServiceCollection services)
    {
        var assembly = typeof(AddItemToBasketCommand).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        // The whole pipeline: Validation -> Logging -> handler. No Transaction (there is no database) and no caching
        // behaviors — the cache was a second Redis copy of a Redis document (Basket audit S5, D5).
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
