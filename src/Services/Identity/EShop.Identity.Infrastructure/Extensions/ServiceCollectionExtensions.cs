using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.Infrastructure.Repositories;
using EShop.Identity.Infrastructure.Services;
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
        IConfiguration configuration)
    {
        // Add DbContext
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("IdentityDb")));

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

        // Add repositories
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Add services
        services.AddScoped<ITokenService, TokenService>();

        return services;
    }
}
