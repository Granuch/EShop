using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.Infrastructure.Repositories;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.Catalog.Infrastructure.Extensions;

/// <summary>
/// Extension methods for adding Catalog infrastructure services
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogInfrastructure(
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
            var dbName = inMemoryDatabaseName ?? $"CatalogTestDb_{Guid.NewGuid()}";
            services.AddDbContext<CatalogDbContext>(options =>
                options.UseInMemoryDatabase(dbName));
        }
        else
        {
            services.AddDbContext<CatalogDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("CatalogDb")));
        }

        // Add repositories
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // Register IUnitOfWork (implemented by CatalogDbContext via BaseDbContext)
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CatalogDbContext>());

        return services;
    }
}
