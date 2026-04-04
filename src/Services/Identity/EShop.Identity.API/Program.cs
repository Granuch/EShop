using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.Infrastructure.Extensions;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Application.Extensions;
using EShop.Identity.Application.Telemetry;
using EShop.Identity.API.Infrastructure.HealthChecks;
using EShop.Identity.API.Infrastructure.Metrics;
using EShop.Identity.API.Infrastructure.Middleware;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text;
using System.Threading.RateLimiting;
using System.Net;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Prometheus;
using HealthChecks.UI.Client;
using StackExchange.Redis;

// Pre-warm thread pool to prevent saturation spikes under burst traffic.
// Without this, .NET adds ~1 thread/500ms causing request queueing at >150 concurrent connections.
ThreadPool.SetMinThreads(workerThreads: 100, completionPortThreads: 100);

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
    builder.Configuration.AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.Local.json",
        optional: true,
        reloadOnChange: true);

    // Configure Serilog from appsettings.json
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "EShop.Identity.API"));

    var forwardedProxies = builder.Configuration
        .GetSection("ForwardedHeaders:KnownProxies")
        .Get<string[]>() ?? [];

    if (forwardedProxies.Length > 0)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (var proxy in forwardedProxies)
            {
                if (IPAddress.TryParse(proxy, out var ipAddress))
                {
                    options.KnownProxies.Add(ipAddress);
                }
            }
        });
    }
    else
    {
        Log.Warning("Forwarded headers are not configured with known proxies. X-Forwarded-For will be ignored.");
    }

    // Add Infrastructure services (DbContext, Identity, Token Service, etc.)
    var useInMemoryDb = builder.Environment.IsEnvironment("Testing");
    var suppressPendingModelChangesWarning = builder.Environment.IsDevelopment()
        || builder.Environment.IsEnvironment("Sandbox");

    builder.Services.AddIdentityInfrastructure(
        builder.Configuration,
        useInMemoryDatabase: useInMemoryDb,
        suppressPendingModelChangesWarning: suppressPendingModelChangesWarning,
        isDevelopment: builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"),
        isSandbox: builder.Environment.IsEnvironment("Sandbox"));

    // Configure Token Cleanup Settings
    builder.Services.Configure<EShop.Identity.Infrastructure.Configuration.TokenCleanupSettings>(
        builder.Configuration.GetSection(EShop.Identity.Infrastructure.Configuration.TokenCleanupSettings.SectionName));

    // Add Background Services
    // Only run cleanup service in non-Testing environments
    if (!useInMemoryDb)
    {
        builder.Services.AddHostedService<EShop.Identity.Infrastructure.BackgroundJobs.ExpiredTokenCleanupService>();
        Log.Information("Expired Token Cleanup Service registered");
    }

    // Add Distributed Cache for brute-force protection
    // Production/Sandbox: Redis for multi-instance horizontal scaling
    // Testing: In-memory cache
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

    // Validate connection strings in production-like environments
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        var identityDbConn = builder.Configuration.GetConnectionString("IdentityDb");
        if (string.IsNullOrEmpty(identityDbConn) || identityDbConn.Contains("#{"))
        {
            throw new InvalidOperationException(
                $"IdentityDb connection string is not configured or contains unresolved placeholder in {builder.Environment.EnvironmentName}.");
        }

        if (string.IsNullOrEmpty(redisConnectionString) || redisConnectionString.Contains("#{"))
        {
            Log.Warning("Redis connection string is not configured or contains unresolved placeholder in {Environment}. " +
                "Distributed cache will not work correctly in multi-instance deployments.",
                builder.Environment.EnvironmentName);
        }
    }

    if (!string.IsNullOrEmpty(redisConnectionString) && !builder.Environment.IsEnvironment("Testing"))
    {
        Log.Information("Configuring Redis distributed cache: {RedisEndpoint}", 
            redisConnectionString.Split(',')[0]); // Log only host, not password

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "EShop_Identity_";

            // Advanced connection configuration for production reliability
            options.ConfigurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString);
            options.ConfigurationOptions.AbortOnConnectFail = false;
            options.ConfigurationOptions.ConnectTimeout = 5000;
            options.ConfigurationOptions.SyncTimeout = 5000;
            options.ConfigurationOptions.ConnectRetry = 3;
            options.ConfigurationOptions.KeepAlive = 60;
            options.ConfigurationOptions.ReconnectRetryPolicy = new StackExchange.Redis.LinearRetry(5000);

            // Enable command logging for troubleshooting (disable in production if not needed)
            // options.ConfigurationOptions.ClientName = $"EShop_Identity_{Environment.MachineName}";
        });

        Log.Information("Redis distributed cache configured successfully");
    }
    else if (builder.Environment.IsEnvironment("Testing"))
    {
        // Testing only: Use in-memory distributed cache
        Log.Warning("Using in-memory distributed cache for Testing environment");
        builder.Services.AddDistributedMemoryCache();
    }
    else
    {
        // Redis connection string not found
        Log.Warning("Redis connection string not configured. Using in-memory cache. NOT suitable for multi-instance!");
        builder.Services.AddDistributedMemoryCache();
    }

    // Add Application services (MediatR, FluentValidation, etc.)
    builder.Services.AddIdentityApplication();

    // Add MassTransit with RabbitMQ messaging
    builder.Services.AddIdentityMessaging(
        builder.Configuration,
        builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));

    // Add OpenTelemetry distributed tracing (Jaeger via OTLP)
    builder.Services.AddEShopOpenTelemetry(
        builder.Configuration,
        serviceName: "EShop.Identity.API",
        serviceVersion: "1.0.0",
        environment: builder.Environment,
        additionalSources: "EShop.Identity");

    // Add Metrics
    builder.Services.AddSingleton<IIdentityMetrics, IdentityMetrics>();

    // Configure JWT Authentication
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;

    // Startup-time validation for critical JWT configuration
    if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
    {
        throw new InvalidOperationException(
            "JWT SecretKey is not configured. Set JwtSettings:SecretKey in configuration or environment variables.");
    }

    if (jwtSettings.SecretKey.Length < 32)
    {
        throw new InvalidOperationException(
            $"JWT SecretKey must be at least 32 characters (256 bits) for HS256. Current length: {jwtSettings.SecretKey.Length}.");
    }

    // Detect placeholder patterns that must be replaced before deployment
    var placeholderPatterns = new[] { "#{" , "CHANGE_ME", "YOUR_", "TestKey", "placeholder" };
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        foreach (var pattern in placeholderPatterns)
        {
            if (jwtSettings.SecretKey.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"JWT SecretKey contains placeholder pattern '{pattern}'. Replace with a secure secret before deploying to {builder.Environment.EnvironmentName}.");
            }
        }
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
    builder.Services.AddAuthorization();

    // Add Rate Limiting (disable in Testing to avoid throttling integration tests)
    if (!builder.Environment.IsEnvironment("Testing"))
    {
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
    }

    // Add CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

            if (allowedOrigins.Length == 0 &&
                !builder.Environment.IsDevelopment() &&
                !builder.Environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins is empty in {builder.Environment.EnvironmentName}. " +
                    "Configure allowed origins before deploying to non-development environments.");
            }

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

    // Add Redis health check if Redis is configured
    if (!string.IsNullOrEmpty(redisConnectionString) && !builder.Environment.IsEnvironment("Testing"))
    {
        healthChecksBuilder.AddRedis(
            redisConnectionString,
            name: "redis",
            tags: ["cache", "ready"]);
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

    // Apply database migrations automatically (Production/Development/Sandbox)
    // Skip for Testing environment (uses in-memory database)
    if (!useInMemoryDb)
    {
        try
        {
            Log.Information("Applying database migrations...");
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            // Apply pending migrations
            await dbContext.Database.MigrateAsync();
            Log.Information("Database migrations applied successfully");

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            // Seed admin user only in Development and Sandbox environments (roles included)
            if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Sandbox"))
            {
                Log.Information("Seeding default roles and admin user...");
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                await SeedData.SeedRolesAndAdminAsync(roleManager, userManager, app.Configuration, Log.Logger);
            }
            else
            {
                // Seed default roles in all other non-testing environments
                Log.Information("Seeding default roles...");
                await SeedData.SeedRolesAsync(roleManager, Log.Logger);
            }

            Log.Information("Seeding completed successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to apply database migrations or seed data");
            throw;
        }
    }

    // Initialize telemetry
    var metrics = app.Services.GetRequiredService<IIdentityMetrics>();
    IdentityTelemetry.Initialize(metrics);

    // Global Exception Handler - must be first middleware
    app.UseGlobalExceptionHandler();

    if (forwardedProxies.Length > 0)
    {
        app.UseForwardedHeaders();
    }

    // Uniform Response Timing - prevents account enumeration through timing attacks
    // Must come early in pipeline to measure total response time
    app.UseMiddleware<UniformResponseTimingMiddleware>();

    // Add Serilog request logging
    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, _, exception) =>
        {
            if (exception != null || httpContext.Response.StatusCode >= 500)
            {
                return LogEventLevel.Error;
            }

            var path = httpContext.Request.Path.Value;
            if (!string.IsNullOrEmpty(path)
                && (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/prometheus", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase)))
            {
                return LogEventLevel.Debug;
            }

            if (httpContext.Response.StatusCode >= 400)
            {
                return LogEventLevel.Warning;
            }

            return LogEventLevel.Information;
        };

        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress?.ToString());
            diagnosticContext.Set("TraceId", System.Diagnostics.Activity.Current?.TraceId.ToString());
            diagnosticContext.Set("SpanId", System.Diagnostics.Activity.Current?.SpanId.ToString());
        };
    });


    // Configure the HTTP request pipeline
    // OpenAPI and Scalar UI - available in Development and Production (not in Testing)
    if (!app.Environment.IsEnvironment("Testing"))
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
    if (!app.Environment.IsEnvironment("Testing"))
    {
        app.UseRateLimiter();
    }

    // Add CORS
    app.UseCors("AllowFrontend");

    app.UseHttpsRedirection();

    // Add Prometheus HTTP metrics middleware (prometheus-net custom business metrics)
    app.UseHttpMetrics(options =>
    {
        options.AddCustomLabel("service", context => "identity");
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Map Prometheus metrics endpoints:
    // /metrics/prom — prometheus-net custom business metrics (identity_login_attempts_total, etc.)
    // In .NET 10, /metrics is auto-registered by the framework for OpenTelemetry metrics,
    // so custom prometheus-net metrics use a separate path to avoid being overridden.
    app.MapMetrics("/prometheus");
    // /metrics/otel — OpenTelemetry metrics (http.server.request.duration, process.runtime.*, etc.)
    app.UseEShopOpenTelemetryPrometheus();

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
        ResponseWriter = (context, report) =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(
                System.Text.Json.JsonSerializer.Serialize(new { status = report.Status.ToString() }));
        }
    });

    // Root endpoint - API info and available endpoints
    app.MapGet("/", () => Results.Ok(new
    {
        service = "EShop Identity API",
        version = "1.0.0",
        environment = app.Environment.EnvironmentName,
        endpoints = new
        {
            documentation = !app.Environment.IsEnvironment("Testing") ? "/scalar/v1" : "Not available in Testing",
            openapi = !app.Environment.IsEnvironment("Testing") ? "/openapi/v1.json" : "Not available in Testing",
            health = "/health",
            healthReady = "/health/ready",
            healthLive = "/health/live",
            metrics = new { prometheus = "/prometheus", otel = "/metrics" },
            authentication = new
            {
                register = "POST /api/v1/auth/register",
                login = "POST /api/v1/auth/login",
                refresh = "POST /api/v1/auth/refresh",
                logout = "POST /api/v1/auth/logout"
            }
        }
    }))
    .WithName("GetApiInfo")
    .WithTags("Info")
    .Produces<object>(StatusCodes.Status200OK);

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
