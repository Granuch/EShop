using EShop.BuildingBlocks.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Notification.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        // Admin panel S12. Until this stage Notification registered validators that nothing ever ran: MediatR had no
        // pipeline at all here, because the consumers call their collaborators directly rather than through it. The
        // journal queries are the first requests to go through MediatR, so they are the first that need it.
        //
        // Validation -> Logging -> handler, Basket's pipeline. No TransactionBehavior: every write in this service is
        // its own commit by design (audit S2, D1), and no caching behaviors — Notification registers no
        // ICacheKeyVersionProvider, so a declared family would evict nothing and log success.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
