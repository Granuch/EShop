using EShop.BuildingBlocks.Domain;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.Infrastructure.Repositories;
using EShop.Identity.Infrastructure.Services;
using EShop.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        string? inMemoryDatabaseName = null)
    {
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
                options.UseNpgsql(configuration.GetConnectionString("IdentityDb")));
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

        // Add services
        services.AddScoped<ITokenService, TokenService>();

        // Add security services
        services.AddScoped<ILoginAttemptTracker, LoginAttemptTracker>();

        return services;
    }
}
