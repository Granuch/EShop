using EShop.Basket.Application.Commands.AddItemToBasket;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
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

        services.Configure<CachingBehaviorOptions>(options =>
        {
            options.KeyPrefix = "basket:";
            options.Version = "v1";
            options.UseVersioning = true;
            options.DefaultDuration = TimeSpan.FromMinutes(2);
        });

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
