using System.Net;
using System.Text;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.Ordering.API.Endpoints;
using EShop.Ordering.API.Infrastructure.Configuration;
using EShop.Ordering.API.Infrastructure.HealthChecks;
using EShop.Ordering.API.Infrastructure.Middleware;
using EShop.Ordering.Application.Extensions;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.Infrastructure.Extensions;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

// Pre-warm thread pool to prevent saturation spikes under burst traffic.
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
    Log.Information("Starting Ordering Service...");

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
        .Enrich.WithProperty("Application", "EShop.Ordering.API"));

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

    // Add Infrastructure services (DbContext, Repositories, IUnitOfWork, etc.)
    var useInMemoryDb = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddOrderingInfrastructure(builder.Configuration, useInMemoryDatabase: useInMemoryDb);

    // Add Application services (MediatR, FluentValidation, Pipeline Behaviors)
    builder.Services.AddOrderingApplication();

    // Add MassTransit with RabbitMQ messaging
    builder.Services.AddOrderingMessaging(
        builder.Configuration,
        builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));

    // Add OpenTelemetry distributed tracing (Jaeger via OTLP)
    builder.Services.AddEShopOpenTelemetry(
        builder.Configuration,
        serviceName: "EShop.Ordering.API",
        serviceVersion: "1.0.0",
        environment: builder.Environment,
        additionalSources: "EShop.Ordering");

    // Validate connection strings in production-like environments
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        var orderingDbConn = builder.Configuration.GetConnectionString("OrderingDb");
        if (string.IsNullOrEmpty(orderingDbConn) || orderingDbConn.Contains("#{"))
        {
            throw new InvalidOperationException(
                $"OrderingDb connection string is not configured or contains unresolved placeholder in {builder.Environment.EnvironmentName}.");
        }

        if (string.IsNullOrEmpty(redisConnectionString) || redisConnectionString.Contains("#{"))
        {
            Log.Warning("Redis connection string is not configured or contains unresolved placeholder in {Environment}. " +
                "Distributed cache will not work correctly in multi-instance deployments.",
                builder.Environment.EnvironmentName);
        }
    }

    // Distributed Cache (Redis)

    if (!string.IsNullOrEmpty(redisConnectionString) && !builder.Environment.IsEnvironment("Testing"))
    {
        Log.Information("Configuring Redis distributed cache: {RedisEndpoint}",
            redisConnectionString.Split(',')[0]);

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "EShop_Ordering_";

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

    // Configure JWT Authentication
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;

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

    // Add Health Checks
    var healthChecksBuilder = builder.Services.AddHealthChecks();

    if (!useInMemoryDb)
    {
        healthChecksBuilder.AddNpgSql(
            builder.Configuration.GetConnectionString("OrderingDb")!,
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
        .AddCheck<OrderingReadinessHealthCheck>(
            "ordering-readiness",
            tags: ["ready"])
        .AddCheck<OrderingLivenessHealthCheck>(
            "ordering-liveness",
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
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

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

    if (forwardedProxies.Length > 0)
    {
        app.UseForwardedHeaders();
    }

    // Add Serilog request logging
    app.UseSerilogRequestLogging(options =>
    {
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

    // OpenAPI and Scalar UI
    if (!app.Environment.IsEnvironment("Testing"))
    {
        app.MapOpenApi();

        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("EShop Ordering API")
                .WithTheme(ScalarTheme.Purple)
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                .WithOpenApiRoutePattern("/openapi/{documentName}.json");
        });

        Log.Information("Scalar API documentation available at /scalar/v1");
    }

    app.UseCors("AllowFrontend");

    app.UseHttpsRedirection();

    // Add Prometheus HTTP metrics middleware
    app.UseHttpMetrics(options =>
    {
        options.AddCustomLabel("service", context => "ordering");
    });

    app.UseAuthentication();
    app.UseAuthorization();

    // Map Order endpoints
    app.MapOrderEndpoints();

    // Map Prometheus metrics endpoints:
    // /prometheus — prometheus-net custom business metrics
    // In .NET 10, /metrics is auto-registered by the framework for OpenTelemetry metrics,
    // so custom prometheus-net metrics use a separate path to avoid being overridden.
    app.MapMetrics("/prometheus");
    // /metrics — OpenTelemetry metrics
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
        service = "EShop Ordering API",
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
            orders = new
            {
                create = "POST /api/v1/orders",
                getById = "GET /api/v1/orders/{id}",
                getAll = "GET /api/v1/orders (Admin)",
                getByUser = "GET /api/v1/users/{userId}/orders",
                addItem = "POST /api/v1/orders/{id}/items",
                removeItem = "DELETE /api/v1/orders/{id}/items/{itemId}",
                cancel = "POST /api/v1/orders/{id}/cancel",
                ship = "POST /api/v1/orders/{id}/ship (Admin)"
            }
        }
    }))
    .WithName("GetApiInfo")
    .WithTags("Info")
    .Produces<object>(StatusCodes.Status200OK);

    Log.Information("Ordering Service started successfully");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
