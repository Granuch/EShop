using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using EShop.Catalog.API.Endpoints;
using EShop.Catalog.API.Infrastructure.Configuration;
using EShop.Catalog.API.Infrastructure.HealthChecks;
using EShop.Catalog.API.Infrastructure.Middleware;
using EShop.Catalog.Application.Extensions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Infrastructure.Caching;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.Infrastructure.Extensions;
using HealthChecks.UI.Client;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

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
    Log.Information("Starting Catalog Service...");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog from appsettings.json
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "EShop.Catalog.API"));

    // Add Infrastructure services (DbContext, Repositories, IUnitOfWork, etc.)
    var useInMemoryDb = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddCatalogInfrastructure(builder.Configuration, useInMemoryDatabase: useInMemoryDb);

    // Add Application services (MediatR, FluentValidation, Pipeline Behaviors)
    builder.Services.AddCatalogApplication();

    // Add Distributed Cache
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

    if (!string.IsNullOrEmpty(redisConnectionString) && !builder.Environment.IsEnvironment("Testing"))
    {
        Log.Information("Configuring Redis distributed cache: {RedisEndpoint}",
            redisConnectionString.Split(',')[0]);

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "EShop_Catalog_";

            options.ConfigurationOptions = ConfigurationOptions.Parse(redisConnectionString);
            options.ConfigurationOptions.AbortOnConnectFail = false;
            options.ConfigurationOptions.ConnectTimeout = 5000;
            options.ConfigurationOptions.SyncTimeout = 5000;
            options.ConfigurationOptions.ConnectRetry = 3;
            options.ConfigurationOptions.KeepAlive = 60;
            options.ConfigurationOptions.ReconnectRetryPolicy = new LinearRetry(5000);
        });

        Log.Information("Redis distributed cache configured successfully");
    }
    else if (builder.Environment.IsEnvironment("Testing"))
    {
        Log.Warning("Using in-memory distributed cache for Testing environment");
        builder.Services.AddDistributedMemoryCache();
    }
    else
    {
        Log.Warning("Redis connection string not configured. Using in-memory cache. NOT suitable for multi-instance!");
        builder.Services.AddDistributedMemoryCache();
    }

    // Wrap IDistributedCache with circuit breaker to prevent
    // cascading timeouts when Redis is down (3 failures → 30s cooldown)
    builder.Services.AddCircuitBreakingCache(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(30));

    // Add Mapster
    var mapsterConfig = TypeAdapterConfig.GlobalSettings;
    mapsterConfig.Scan(Assembly.GetExecutingAssembly());
    mapsterConfig.Scan(typeof(ProductDto).Assembly);
    builder.Services.AddSingleton(mapsterConfig);
    builder.Services.AddScoped<IMapper, ServiceMapper>();

    // Configure JWT Authentication
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;

    if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
    {
        throw new InvalidOperationException(
            "JWT SecretKey is not configured. Set JwtSettings:SecretKey in configuration or environment variables.");
    }

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
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
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

    // Add Rate Limiting (disable in Testing to avoid throttling integration tests)
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Global rate limiter — 100 requests per minute per IP
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            // Stricter rate limiter for search queries — 30 per minute
            options.AddFixedWindowLimiter("search", limiterOptions =>
            {
                limiterOptions.AutoReplenishment = true;
                limiterOptions.PermitLimit = 30;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
            });
        });
    }

    // Add Health Checks
    var healthChecksBuilder = builder.Services.AddHealthChecks();

    if (!useInMemoryDb)
    {
        healthChecksBuilder.AddNpgSql(
            builder.Configuration.GetConnectionString("CatalogDb")!,
            name: "postgresql",
            tags: ["db", "ready"]);
    }

    if (!string.IsNullOrEmpty(redisConnectionString) && !builder.Environment.IsEnvironment("Testing"))
    {
        healthChecksBuilder.AddRedis(
            redisConnectionString,
            name: "redis",
            tags: ["cache", "ready"]);
    }

    healthChecksBuilder
        .AddCheck<CatalogReadinessHealthCheck>(
            "catalog-readiness",
            tags: ["ready"])
        .AddCheck<CatalogLivenessHealthCheck>(
            "catalog-liveness",
            tags: ["live"]);

    // Add OpenAPI
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();

    var app = builder.Build();

    // Apply database migrations automatically
    if (!useInMemoryDb)
    {
        try
        {
            Log.Information("Applying database migrations...");
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await dbContext.Database.MigrateAsync();
            Log.Information("Database migrations applied successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to apply database migrations");
            throw;
        }
    }

    // Global Exception Handler - must be first middleware
    app.UseGlobalExceptionHandler();

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

    // OpenAPI and Scalar UI
    if (!app.Environment.IsEnvironment("Testing"))
    {
        app.MapOpenApi();

        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("EShop Catalog API")
                .WithTheme(ScalarTheme.Purple)
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                .WithOpenApiRoutePattern("/openapi/{documentName}.json");
        });

        Log.Information("Scalar API documentation available at /scalar/v1");
    }

    // Add CORS
    app.UseCors("AllowFrontend");

    // Rate Limiting
    if (!app.Environment.IsEnvironment("Testing"))
    {
        app.UseRateLimiter();
    }

    app.UseHttpsRedirection();

    app.UseAuthentication();
    app.UseAuthorization();

    // Map Minimal API endpoints
    app.MapProductEndpoints();
    app.MapCategoryEndpoints();

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

    // Root endpoint - API info
    app.MapGet("/", () => Results.Ok(new
    {
        service = "EShop Catalog API",
        version = "1.0.0",
        environment = app.Environment.EnvironmentName,
        endpoints = new
        {
            documentation = !app.Environment.IsEnvironment("Testing") ? "/scalar/v1" : "Not available in Testing",
            openapi = !app.Environment.IsEnvironment("Testing") ? "/openapi/v1.json" : "Not available in Testing",
            health = "/health",
            healthReady = "/health/ready",
            healthLive = "/health/live",
            products = "GET /api/v1/products",
            categories = "GET /api/v1/categories"
        }
    }))
    .WithName("GetApiInfo")
    .WithTags("Info")
    .Produces<object>(StatusCodes.Status200OK);

    Log.Information("Catalog Service started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Catalog Service terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
