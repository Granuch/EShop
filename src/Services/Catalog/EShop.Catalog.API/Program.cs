using System.Data;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.Catalog.API.Endpoints;
using EShop.Catalog.API.Infrastructure.Configuration;
using EShop.Catalog.API.Infrastructure.HealthChecks;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Catalog.Application.Extensions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Infrastructure.Caching;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.Infrastructure.Extensions;
using HealthChecks.UI.Client;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
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
    Log.Information("Starting Catalog Service...");

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
        .Enrich.WithProperty("Application", "EShop.Catalog.API"));

    // Shared across every service — reads KnownNetworks as well as KnownProxies, which is what
    // works under Docker/Kubernetes, and logs rather than silently dropping an unparseable entry.
    // Also replaces the obsolete ForwardedHeadersOptions.KnownNetworks this used to call.
    var forwardedHeadersEnabled = builder.Services.AddEShopForwardedHeaders(builder.Configuration);

    // CacheInvalidation FIRST, then Application, then Infrastructure. MediatR runs pipeline
    // behaviors in DI registration order (first registered = outermost), so these three calls are
    // what sets the pipeline:
    //   CacheInvalidation -> Transaction -> Validation -> Logging -> Caching -> handler
    // CacheInvalidationBehavior has to be outermost because it invalidates AFTER the handler
    // returns: registered inside TransactionBehavior it evicted keys and bumped DEBT-16 family
    // versions before the write committed, so a concurrent read could repopulate the cache with
    // pre-commit data under the new version and keep serving it for the full TTL.
    // Application before Infrastructure is the older, separate fix: Infrastructure first produced
    // Caching -> ... -> Transaction -> Validation, i.e. validation running after the transaction
    // had already opened. Both orderings are silent if broken — nothing fails, and neither is
    // visible without reading all three extension methods.
    builder.Services.AddEShopCacheInvalidation();
    builder.Services.AddCatalogApplication();

    // Add Infrastructure services (DbContext, Repositories, IUnitOfWork, etc.)
    // Testing defaults to the InMemory provider, but a test host can opt into a real relational
    // database with Testing:UseRelationalDatabase=true. Same switch, and same reason, as Identity:
    // this service ships production paths that only a relational provider can execute —
    // EF.Functions.ILike in ProductQueryService, the unique/GIN indexes on Products, the partial
    // unique index behind SetMainProductImageCommandHandler's two-save demotion, decimal(18,2)
    // precision and the column length caps. On InMemory none of them is reachable from a test.
    // Deliver the flag with UseSetting: this line runs while the app is being composed, so a
    // ConfigureAppConfiguration source arrives too late and the default silently wins.
    var useInMemoryDb = builder.Environment.IsEnvironment("Testing")
        && !builder.Configuration.GetValue<bool>("Testing:UseRelationalDatabase");
    builder.Services.AddCatalogInfrastructure(builder.Configuration, useInMemoryDatabase: useInMemoryDb);

    // Add MassTransit with RabbitMQ messaging
    builder.Services.AddCatalogMessaging(
        builder.Configuration,
        builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));

    // Add OpenTelemetry distributed tracing (Jaeger via OTLP)
    builder.Services.AddEShopOpenTelemetry(
        builder.Configuration,
        serviceName: "EShop.Catalog.API",
        serviceVersion: "1.0.0",
        environment: builder.Environment,
        additionalSources: "EShop.Catalog");

    // Validate connection strings in production-like environments
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        var catalogDbConn = builder.Configuration.GetConnectionString("CatalogDb");
        if (string.IsNullOrEmpty(catalogDbConn) || catalogDbConn.Contains("#{"))
        {
            throw new InvalidOperationException(
                $"CatalogDb connection string is not configured or contains unresolved placeholder in {builder.Environment.EnvironmentName}.");
        }

        if (string.IsNullOrEmpty(redisConnectionString) || redisConnectionString.Contains("#{"))
        {
            Log.Warning("Redis connection string is not configured or contains unresolved placeholder in {Environment}. " +
                "Distributed cache will not work correctly in multi-instance deployments.",
                builder.Environment.EnvironmentName);
        }
    }

    // Add Distributed Cache

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

    if (jwtSettings.SecretKey.Length < 32)
    {
        throw new InvalidOperationException(
            $"JWT SecretKey must be at least 32 characters (256 bits) for HS256. Current length: {jwtSettings.SecretKey.Length}.");
    }

    // Detect placeholder patterns that must be replaced before deployment
    var placeholderPatterns = new[] { "#{", "CHANGE_ME", "YOUR_", "TestKey", "placeholder" };
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

    // Add Rate Limiting (permissive in Testing to avoid throttling integration tests).
    // Limits are read from configuration so a test host can make them assertable, matching
    // Identity's RateLimiting:* keys — without that the limiters are unreachable from any test and
    // the partition key, which is the part that actually matters, is unverifiable.
    var rateLimitingEnabled = !builder.Environment.IsEnvironment("Testing")
        || builder.Configuration.GetValue<bool>("RateLimiting:EnableInTesting");
    var globalPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 100;
    var globalWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Global:WindowSeconds") ?? 60;
    var searchPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Search:PermitLimit")
        ?? (rateLimitingEnabled ? 30 : int.MaxValue);
    var searchWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Search:WindowSeconds") ?? 60;

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        if (rateLimitingEnabled)
        {
            // Global rate limiter — 100 requests per minute per client.
            // Partition on GetClientPartitionKey, not on RemoteIpAddress directly: the helper
            // normalises IPv4-mapped IPv6, so ::ffff:1.2.3.4 and 1.2.3.4 share one bucket instead
            // of a dual-stack client silently getting two allowances. The "search" policy below
            // already used it; this line did not, so the two only matched in shape.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: EShopForwardedHeaders.GetClientPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = globalPermitLimit,
                        Window = TimeSpan.FromSeconds(globalWindowSeconds)
                    }));
        }

        // Named rate limiter for search queries — 30 per minute in production.
        // AddFixedWindowLimiter(name, ...) builds ONE bucket shared by every caller — it has no
        // partition key — so a single client could exhaust the search allowance for the whole
        // service and lock everyone else out. AddPolicy<string> with an explicit partition key is
        // the partitioned form, matching the GlobalLimiter directly above.
        options.AddPolicy<string>("search", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: EShopForwardedHeaders.GetClientPartitionKey(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = searchPermitLimit,
                    Window = TimeSpan.FromSeconds(searchWindowSeconds)
                }));
    });

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

    // Reject unknown JSON properties instead of silently dropping them. By default
    // System.Text.Json ignores an unmapped member, so a typo'd or stale field name
    // ("descriptionn", "isMainImage") is accepted with a 201 and the value is never stored —
    // the caller has no way to notice. Disallow turns that into a JsonException, which minimal
    // API model binding wraps in BadHttpRequestException; GlobalExceptionHandlerMiddleware has
    // a branch mapping that to 400 with the offending property name in Detail.
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    });

    // Without this, minimal API binding swallows the JsonException and writes a bare 400 with
    // an EMPTY body — the caller learns the request was rejected but not which property caused
    // it, which is the same opacity problem as the old DomainError responses. Throwing instead
    // routes the failure through GlobalExceptionHandlerMiddleware's BadHttpRequestException
    // branch, which returns problem+json naming the offending member.
    builder.Services.Configure<RouteHandlerOptions>(options =>
    {
        options.ThrowOnBadRequest = true;
    });

    // Add OpenAPI
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();

    // Catalog has the widest branch set, and the order is load-bearing throughout: mappers are
// first-match-wins. AddEfConcurrency must precede AddEfDuplicateKey (DbUpdateConcurrencyException
// derives from DbUpdateException), and AddProductSkuConflict must precede it too, since
// AddEfDuplicateKey matches every unique violation and would report a lost SKU race as a generic
// DuplicateResource. AddMalformedJsonBody pairs with ThrowOnBadRequest +
// UnmappedMemberHandling.Disallow configured above.
builder.Services.AddEShopProblemDetails(options => options
    .AddCommon()
    .AddNotFound()
    .AddEfConcurrency()
    .AddProductSkuConflict()
    .AddEfDuplicateKey()
    .AddMalformedJsonBody());

var app = builder.Build();

    // Apply database migrations automatically
    if (!useInMemoryDb)
    {
        const int maxMigrationAttempts = 8;
        var migrationDelay = TimeSpan.FromSeconds(5);

        for (var attempt = 1; attempt <= maxMigrationAttempts; attempt++)
        {
            try
            {
                Log.Information("Applying database migrations...");
                using var scope = app.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

                var hasMigrations = dbContext.Database.GetMigrations().Any();
                if (!hasMigrations)
                {
                    if (app.Environment.IsDevelopment()
                        || app.Environment.IsEnvironment("Testing")
                        || app.Environment.IsEnvironment("Sandbox"))
                    {
                        Log.Warning("No EF Core migrations found for CatalogDbContext. Using EnsureCreated for {Environment}.",
                            app.Environment.EnvironmentName);

                        var missingTables = await CatalogSchemaMissingAsync(dbContext);
                        if (missingTables)
                        {
                            await dbContext.Database.EnsureCreatedAsync();
                        }
                        else
                        {
                            Log.Information("Catalog schema already exists. Skipping EnsureCreated.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"No EF Core migrations found for CatalogDbContext in {app.Environment.EnvironmentName}. " +
                            "Add migrations before deploying to non-development environments. " +
                            "EnsureCreated is not allowed in production-like environments to prevent schema drift.");
                    }
                }
                else
                {
                    await dbContext.Database.MigrateAsync();
                }

                Log.Information("Database schema ensured successfully");
                break;
            }
            catch (Exception ex) when (IsPostgresStartupException(ex) && attempt < maxMigrationAttempts)
            {
                Log.Warning(ex,
                    "Database is not ready yet (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}...",
                    attempt,
                    maxMigrationAttempts,
                    migrationDelay);

                await Task.Delay(migrationDelay);
                migrationDelay += TimeSpan.FromSeconds(5);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to apply database migrations");
                throw;
            }
        }
    }

    // Global Exception Handler - must be first middleware
    app.UseGlobalExceptionHandler();

    app.UseEShopRequestLogging();

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

static bool IsPostgresStartupException(Exception exception)
{
    if (exception is PostgresException { SqlState: "57P03" })
    {
        return true;
    }

    return exception.InnerException is not null
        && IsPostgresStartupException(exception.InnerException);
}

    // Forwarded Headers — must be before other middleware that depend on correct scheme/IP
    app.UseEShopForwardedHeaders(forwardedHeadersEnabled);

    // Add CORS
    app.UseCors("AllowFrontend");

    // Rate Limiting
    app.UseRateLimiter();

    var httpsPort = app.Configuration["ASPNETCORE_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORT"];
    if (!string.IsNullOrWhiteSpace(httpsPort))
    {
        app.UseHttpsRedirection();
    }

    // Add Prometheus HTTP metrics middleware (prometheus-net custom business metrics)
    app.UseHttpMetrics(options =>
    {
        options.AddCustomLabel("service", _ => "catalog");
    });

    app.UseAuthentication();
    app.UseAuthorization();

    // Map Minimal API endpoints
    app.MapProductEndpoints();
    app.MapCategoryEndpoints();

    // Map Prometheus metrics endpoints:
    // /prometheus — prometheus-net custom business metrics (http_requests_received_total, etc.)
    // In .NET 10, /metrics is auto-registered by the framework for OpenTelemetry metrics,
    // so custom prometheus-net metrics use a separate path to avoid being overridden.
    // Both scrape endpoints are anonymous. Restricted to loopback + private networks unless
    // Metrics:AllowedNetworks says otherwise; Testing is exempt (TestServer has no socket).
    app.UseEShopMetricsAccess(app.Configuration, app.Environment);
    app.MapMetrics("/prometheus");
    // /metrics — OpenTelemetry metrics (http.server.request.duration, process.runtime.*, etc.)
    app.UseEShopOpenTelemetryPrometheus();

    // Health check endpoints with detailed response
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = EShopHealthResponseWriter.WriteAsync
    });

    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = EShopHealthResponseWriter.WriteAsync
    });

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live"),
        ResponseWriter = EShopHealthResponseWriter.WriteAsync
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
            metrics = new { prometheus = "/prometheus", otel = "/metrics" },
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

static async Task<bool> CatalogSchemaMissingAsync(CatalogDbContext dbContext)
{
    await dbContext.Database.OpenConnectionAsync();
    try
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('Categories', 'Products', 'ProductImages')
            """;

        var result = await command.ExecuteScalarAsync();
        var count = result is null ? 0 : Convert.ToInt32(result);
        return count < 3;
    }
    finally
    {
        await dbContext.Database.CloseConnectionAsync();
    }
}
