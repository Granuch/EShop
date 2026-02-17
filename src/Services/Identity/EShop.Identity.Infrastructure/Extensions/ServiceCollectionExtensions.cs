using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.HealthChecks;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Consumers;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.Infrastructure.Repositories;
using EShop.Identity.Infrastructure.Services;
using EShop.Identity.Infrastructure.Security;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.Identity.Infrastructure.Extensions;

/// <summary>
/// Extension methods for adding infrastructure services
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryDatabase = false,
        string? inMemoryDatabaseName = null,
        bool suppressPendingModelChangesWarning = false)
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
            var dbName = inMemoryDatabaseName ?? $"IdentityTestDb_{Guid.NewGuid()}";
            services.AddDbContext<IdentityDbContext>(options =>
                options.UseInMemoryDatabase(dbName));
        }
        else
        {
            services.AddDbContext<IdentityDbContext>(options =>
            {
                options.UseNpgsql(configuration.GetConnectionString("IdentityDb"));

                if (suppressPendingModelChangesWarning)
                {
                    options.ConfigureWarnings(warnings =>
                        warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                }
            });
        }

        // Add ASP.NET Core Identity
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            // Password requirements
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 8;

            // User requirements
            options.User.RequireUniqueEmail = true;

            // Sign-in requirements
            options.SignIn.RequireConfirmedEmail = false; // TODO: Set to true when EmailService is configured

            // Lockout settings
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<IdentityDbContext>()
        .AddDefaultTokenProviders();

        // Configure JWT settings
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        // Configure brute-force protection settings
        services.Configure<BruteForceProtectionSettings>(
            configuration.GetSection(BruteForceProtectionSettings.SectionName));

        // Add repositories
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Register IUnitOfWork (implemented by IdentityDbContext)
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<IdentityDbContext>());

        // Register DbContext base type for OutboxProcessorService
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<IdentityDbContext>());

        // Register Outbox Processor background service
        services.AddSingleton(new OutboxProcessorOptions
        {
            BatchSize = 20,
            PollingIntervalMs = 1000,
            MaxRetries = 5,
            ErrorRetryDelayMs = 5000
        });
        services.AddHostedService<OutboxProcessorService>();

        // Register Outbox Cleanup background service
        services.AddSingleton(new OutboxCleanupOptions
        {
            RetentionDays = 7,
            CleanupIntervalHours = 6
        });
        services.AddHostedService<OutboxCleanupService>();

        // Register Outbox health check for dead-letter and depth monitoring
        services.AddSingleton(new OutboxHealthCheckOptions
        {
            DeadLetterWarningThreshold = 10,
            PendingWarningThreshold = 100
        });
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck>(
                "outbox",
                tags: ["ready", "outbox"]);

        // Add services
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ITokenCleanupService, TokenCleanupService>();

        // Add cached services for performance optimization
        services.AddScoped<ICachedUserRolesService, CachedUserRolesService>();
        services.AddSingleton<IRevokedTokenCache, RevokedTokenCache>();

        // Add security services
        services.AddScoped<ILoginAttemptTracker, LoginAttemptTracker>();

        return services;
    }

    /// <summary>
    /// Adds MassTransit with RabbitMQ transport for the Identity service.
    /// </summary>
    public static IServiceCollection AddIdentityMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.AddMessaging<IdentityDbContext>(
            configuration,
            isDevelopment,
            bus =>
            {
                // Register consumers from this assembly
                bus.AddConsumer<ProductCreatedConsumer>();
            });

        return services;
    }
}
