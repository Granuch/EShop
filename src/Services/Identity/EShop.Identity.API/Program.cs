using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.Infrastructure.Extensions;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Application.Extensions;
using EShop.Identity.Application.Telemetry;
using EShop.Identity.API.Infrastructure.HealthChecks;
using EShop.Identity.API.Infrastructure.Metrics;
using EShop.Identity.API.Infrastructure.Middleware;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Threading.RateLimiting;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Prometheus;
using HealthChecks.UI.Client;

// Configure Serilog early to catch startup errors
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Identity Service...");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog from appsettings.json
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "EShop.Identity.API"));

    // Add Infrastructure services (DbContext, Identity, Token Service, etc.)
    var useInMemoryDb = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddIdentityInfrastructure(builder.Configuration, useInMemoryDatabase: useInMemoryDb);

    // Add Distributed Cache for brute-force protection
    // In production, replace with Redis for multi-instance deployments
    // For development/testing, use in-memory cache
    // TODO: Uncomment and add Microsoft.Extensions.Caching.StackExchangeRedis package for production
    /*
    if (builder.Configuration.GetConnectionString("Redis") != null && !builder.Environment.IsDevelopment())
    {
        // Production: Use Redis for distributed caching (horizontal scaling support)
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = builder.Configuration.GetConnectionString("Redis");
            options.InstanceName = "EShop_Identity_";
        });
    }
    else
    */
    {
        // Development/Testing: Use in-memory distributed cache
        // Note: Not suitable for production multi-instance deployments
        builder.Services.AddDistributedMemoryCache();
    }

    // Add Application services (MediatR, FluentValidation, etc.)
    builder.Services.AddIdentityApplication();

    // Add Metrics
    builder.Services.AddSingleton<IIdentityMetrics, IdentityMetrics>();

    // Configure JWT Authentication
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

    // Add Authorization
    builder.Services.AddAuthorization();

    // Add Rate Limiting
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Global rate limiter - 100 requests per minute per IP
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1)
                }));

        // Strict rate limiter for auth endpoints - 10 requests per minute
        options.AddFixedWindowLimiter("auth", limiterOptions =>
        {
            limiterOptions.AutoReplenishment = true;
            limiterOptions.PermitLimit = 10;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
        });

        // Login rate limiter - 5 attempts per minute (more strict)
        options.AddFixedWindowLimiter("login", limiterOptions =>
        {
            limiterOptions.AutoReplenishment = true;
            limiterOptions.PermitLimit = 5;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
        });
    });

    // Add CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });

    // Add Health Checks
    var healthChecksBuilder = builder.Services.AddHealthChecks();

    // Only add PostgreSQL health check if not in Testing environment
    if (!useInMemoryDb)
    {
        healthChecksBuilder.AddNpgSql(
            builder.Configuration.GetConnectionString("IdentityDb")!,
            name: "postgresql",
            tags: ["db", "ready"]);
    }

    healthChecksBuilder
        .AddCheck<IdentityReadinessHealthCheck>(
            "identity-readiness",
            tags: ["ready"])
        .AddCheck<IdentityLivenessHealthCheck>(
            "identity-liveness",
            tags: ["live"]);

    // Add Controllers and OpenAPI
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();

    var app = builder.Build();

    // Initialize telemetry
    var metrics = app.Services.GetRequiredService<IIdentityMetrics>();
    IdentityTelemetry.Initialize(metrics);

    // Global Exception Handler - must be first middleware
    app.UseGlobalExceptionHandler();

    // Uniform Response Timing - prevents account enumeration through timing attacks
    // Must come early in pipeline to measure total response time
    app.UseMiddleware<UniformResponseTimingMiddleware>();

    // Add Serilog request logging
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress?.ToString());
        };
    });

    // Apply database migrations and seed data in Development
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.MigrateAsync();

        // Seed default roles and admin user
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await SeedData.SeedRolesAndAdminAsync(roleManager, userManager, Log.Logger);
    }


    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment())
    {
        // OpenAPI JSON endpoint - must be mapped first
        app.MapOpenApi();

        // Scalar UI for API documentation
        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("EShop Identity API")
                .WithTheme(ScalarTheme.Purple)
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                .WithOpenApiRoutePattern("/openapi/{documentName}.json");
        });

        Log.Information("Scalar API documentation available at /scalar/v1");
    }

    // Add Rate Limiting middleware
    app.UseRateLimiter();

    // Add CORS
    app.UseCors("AllowFrontend");

    app.UseHttpsRedirection();

    // Add Prometheus HTTP metrics middleware
    app.UseHttpMetrics(options =>
    {
        options.AddCustomLabel("service", context => "identity");
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Map Prometheus metrics endpoint
    app.MapMetrics();

    // Health check endpoints with detailed response
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    Log.Information("Identity Service started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Identity Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
