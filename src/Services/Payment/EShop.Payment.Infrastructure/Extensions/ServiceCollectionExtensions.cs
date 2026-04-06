using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.Payment.Application.Consumers;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryDatabase = false,
        string? inMemoryDatabaseName = null)
    {
        services.Configure<PaymentSimulationSettings>(
            configuration.GetSection(PaymentSimulationSettings.SectionName));

        if (useInMemoryDatabase)
        {
            var dbName = inMemoryDatabaseName ?? $"PaymentTestDb_{Guid.NewGuid()}";
            services.AddDbContext<PaymentDbContext>(options => options.UseInMemoryDatabase(dbName));
        }
        else
        {
            services.AddDbContext<PaymentDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("PaymentDb")));
        }

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<PaymentDbContext>());
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<PaymentDbContext>());

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentProcessor, MockPaymentProcessor>();

        services.AddHealthChecks();

        return services;
    }

    public static IServiceCollection AddPaymentMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.AddMessaging<PaymentDbContext>(
            configuration,
            isDevelopment,
            bus =>
            {
                bus.AddConsumer<OrderCreatedConsumer>();
            });

        return services;
    }
}
