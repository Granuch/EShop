using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Identity.Infrastructure.Extensions;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Application.Extensions;
using EShop.Identity.Application.Telemetry;
using EShop.Identity.API.Infrastructure.HealthChecks;
using EShop.Identity.API.Infrastructure.Metrics;
using EShop.Identity.API.Infrastructure.Middleware;
using EShop.Identity.API.Infrastructure.Security;
using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Messaging.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using System.Net;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Prometheus;
using HealthChecks.UI.Client;
using Npgsql;
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

    // Shared across every service — see EShopForwardedHeaders for why KnownNetworks matters
    // under Docker/Kubernetes and why an unparseable entry is logged rather than dropped.
    var forwardedHeadersEnabled = builder.Services.AddEShopForwardedHeaders(builder.Configuration);

    // Application BEFORE Infrastructure, and the order is load-bearing: MediatR runs pipeline
    // behaviors in registration order, Application registers Transaction/Validation/Logging and
    // Infrastructure registers Caching/CacheInvalidation. Registering Infrastructure first
    // produced Caching -> CacheInvalidation -> Transaction -> Validation -> Logging -> handler,
    // i.e. cache lookups outside the transaction and validation running after it had opened.
    // This now matches Basket and Payment. (Catalog and Ordering still have the old call order —
    // same latent defect, out of scope here.)
    builder.Services.AddIdentityApplication();

    // Add Infrastructure services (DbContext, Identity, Token Service, etc.)
    // Testing defaults to the InMemory provider, but a test host can opt into a real relational
    // database with Testing:UseRelationalDatabase=true. That switch exists because the provider
    // choice used to be hardcoded to the environment name, which made every relational-only code
    // path — ExecuteUpdateAsync/ExecuteDeleteAsync, column limits, concurrency tokens —
    // unreachable from any test, and left RefreshTokenRepository and TokenCleanupService carrying
    // IsInMemory() forks so that production and tests ran different queries. Deliver it with
    // builder.UseSetting (host configuration): ConfigureAppConfiguration lands after this read.
    var useInMemoryDb = builder.Environment.IsEnvironment("Testing")
        && !builder.Configuration.GetValue<bool>("Testing:UseRelationalDatabase");
    var suppressPendingModelChangesWarning = builder.Environment.IsDevelopment()
        || builder.Environment.IsEnvironment("Sandbox")
        || builder.Environment.IsEnvironment("Testing");

    builder.Services.AddIdentityInfrastructure(
        builder.Configuration,
        useInMemoryDatabase: useInMemoryDb,
        suppressPendingModelChangesWarning: suppressPendingModelChangesWarning,
        isDevelopment: builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"),
        isSandbox: builder.Environment.IsEnvironment("Sandbox"));

    builder.Services.AddHttpContextAccessor();

    builder.Services.Configure<InternalServiceAuthSettings>(
        builder.Configuration.GetSection(InternalServiceAuthSettings.SectionName));

    var internalServiceAuth = builder.Configuration
        .GetSection(InternalServiceAuthSettings.SectionName)
        .Get<InternalServiceAuthSettings>() ?? new InternalServiceAuthSettings();

    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        if (string.IsNullOrWhiteSpace(internalServiceAuth.ApiKey))
        {
            throw new InvalidOperationException(
                $"{InternalServiceAuthSettings.SectionName}:ApiKey is required in {builder.Environment.EnvironmentName} for internal service authorization.");
        }

        var apiKeyPlaceholderPatterns = new[] { "CHANGE_ME", "LOCAL_", "#{", "REPLACE_WITH_", "YOUR_", "placeholder" };
        foreach (var pattern in apiKeyPlaceholderPatterns)
        {
            if (internalServiceAuth.ApiKey.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{InternalServiceAuthSettings.SectionName}:ApiKey contains placeholder pattern '{pattern}' in {builder.Environment.EnvironmentName}. Replace with a secure value.");
            }
        }
    }

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

        builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
        {
            var redisOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString);
            redisOptions.AbortOnConnectFail = false;
            redisOptions.ConnectTimeout = 5000;
            redisOptions.SyncTimeout = 5000;
            redisOptions.ConnectRetry = 3;
            redisOptions.KeepAlive = 60;
            redisOptions.ReconnectRetryPolicy = new StackExchange.Redis.LinearRetry(5000);
            return StackExchange.Redis.ConnectionMultiplexer.Connect(redisOptions);
        });

        Log.Information("Redis distributed cache configured successfully");
    }
    else if (builder.Environment.IsEnvironment("Testing"))
    {
        // Testing only: Use in-memory distributed cache
        Log.Warning("Using in-memory distributed cache for Testing environment");
        builder.Services.AddDistributedMemoryCache();
    }
    else if (builder.Environment.IsDevelopment())
    {
        // Development keeps the in-memory fallback so the service runs without Redis locally.
        // The brute-force counters are then single-process and non-atomic — fine for one
        // developer, not fine anywhere else. See the throw below.
        Log.Warning("Redis connection string not configured. Using in-memory cache. " +
                    "Brute-force counters are non-atomic and single-process in this mode.");
        builder.Services.AddDistributedMemoryCache();
    }
    else
    {
        // Brute-force protection is the reason this is fatal rather than a warning.
        // LoginAttemptTracker has two code paths: with IConnectionMultiplexer it uses Redis
        // directly (atomic INCR, raw keys); without it, it falls back to IDistributedCache,
        // where (a) the counter is a non-atomic read-modify-write, so concurrent failed logins
        // undercount and the lockout does not fire under exactly the burst it exists to stop,
        // and (b) the keys carry the "EShop_Identity_" InstanceName prefix, a different
        // namespace from the Redis path — so an instance that silently fell back would see an
        // empty counter set and start every attacker from zero.
        // A silently-degrading lockout is worse than a startup failure, so outside
        // Development and Testing this is a hard requirement.
        throw new InvalidOperationException(
            $"ConnectionStrings:Redis is required in {builder.Environment.EnvironmentName}. " +
            "Brute-force protection depends on Redis for atomic counters; the in-memory " +
            "fallback is single-process, non-atomic, and uses a different key namespace.");
    }

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
    var placeholderPatterns = new[] { "#{" , "CHANGE_ME", "LOCAL_", "REPLACE_WITH_", "YOUR_", "TestKey", "placeholder" };
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
            ClockSkew = TimeSpan.Zero,

            // API-12. Pin the algorithm and require a signature. Without ValidAlgorithms the
            // handler accepts any algorithm the key can satisfy, which is the family of confusion
            // attacks this setting exists to close; RequireSignedTokens makes an unsigned token an
            // explicit rejection rather than something that depends on other settings lining up.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,

            // MapInboundClaims (default true) is load-bearing here and was previously implicit:
            // TokenService writes JwtRegisteredClaimNames.Sub, GetCurrentUserId() reads
            // ClaimTypes.NameIdentifier, and the pair only agree because the default inbound
            // mapper rewrites sub -> nameidentifier. Stating the claim types makes the dependency
            // visible instead of accidental — turning MapInboundClaims off without also setting
            // these would silently break every GetCurrentUserId() call.
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role
        };
    });

    // Add Authorization
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("InternalService", policy =>
            policy.Requirements.Add(new InternalServiceRequirement()));
    });

    builder.Services.AddSingleton<IAuthorizationHandler, InternalServiceAuthorizationHandler>();

    var enableRateLimiting = !builder.Environment.IsEnvironment("Testing")
        || builder.Configuration.GetValue<bool>("RateLimiting:EnableInTesting");
    var globalRateLimit = builder.Configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 100;
    var globalRateWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Global:WindowSeconds") ?? 60;
    var authRateLimit = builder.Configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit") ?? 10;
    var authRateWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Auth:WindowSeconds") ?? 60;
    var loginRateLimit = builder.Configuration.GetValue<int?>("RateLimiting:Login:PermitLimit") ?? 5;
    var loginRateWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Login:WindowSeconds") ?? 60;

    var effectiveGlobalRateLimit = enableRateLimiting ? globalRateLimit : int.MaxValue;
    var effectiveAuthRateLimit = enableRateLimiting ? authRateLimit : int.MaxValue;
    var effectiveLoginRateLimit = enableRateLimiting ? loginRateLimit : int.MaxValue;

    // Every limiter below partitions on EShopForwardedHeaders.GetClientPartitionKey.
    // UseForwardedHeaders (configured above) runs before UseRateLimiter, so RemoteIpAddress is the
    // real client whenever a trusted proxy is configured.

    // Add Rate Limiting (Testing uses permissive limits by default, can be hardened for dedicated tests)
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // DOC-01. A rejected request used to return 429 with an EMPTY body: RejectionStatusCode
        // sets the status and nothing writes a payload. So the one response a client is most
        // likely to need to handle programmatically was the only one carrying no errorCode, and
        // scripts/verify-all.sh could not assert on it at all. Emit the same envelope as every
        // other error, and advertise Retry-After when the limiter can tell us the window.
        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            await EShopProblem.WriteAsync(context.HttpContext, EShopProblem.Create(
                context.HttpContext,
                StatusCodes.Status429TooManyRequests,
                detail: "Too many requests. Please retry later.",
                errorCode: "Request.RateLimited"));
        };

        // Global rate limiter
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: EShopForwardedHeaders.GetClientPartitionKey(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = effectiveGlobalRateLimit,
                    Window = TimeSpan.FromSeconds(globalRateWindowSeconds)
                }));

        // Auth endpoints limiter. AddPolicy (not AddFixedWindowLimiter) - the latter builds a
        // single unpartitioned bucket shared by every caller, which lets one client exhaust the
        // auth/login allowance for the whole service.
        options.AddPolicy<string>("auth", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: EShopForwardedHeaders.GetClientPartitionKey(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = effectiveAuthRateLimit,
                    Window = TimeSpan.FromSeconds(authRateWindowSeconds)
                }));

        // Login-specific limiter
        options.AddPolicy<string>("login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: EShopForwardedHeaders.GetClientPartitionKey(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = effectiveLoginRateLimit,
                    Window = TimeSpan.FromSeconds(loginRateWindowSeconds)
                }));
    });

    // Add CORS
    // Validated here rather than inside AddPolicy: CORS builds its policies lazily on first
    // use, so a throw in the lambda is a request-time 500 on a host that already reported
    // healthy. The guard also rejects the placeholder origin shipped in the tracked
    // appsettings.Production.json, which the old "is the array empty?" check accepted.
    var corsAllowedOrigins = CorsOriginGuard.GetValidatedOrigins(builder.Configuration, builder.Environment);

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins(corsAllowedOrigins)
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

    // Identity has never mapped the DbUpdate* exceptions - preserved as-is.
    builder.Services.AddEShopProblemDetails(options => options
        .AddCommon()
        .AddNotFound());

    var app = builder.Build();

    // BUG-03 rail. Email confirmation is deliberately parked scaffolding: RegisterCommandHandler
    // mints a confirmation token and immediately discards it — it is on neither RegisterResponse
    // nor UserRegisteredIntegrationEvent — while the response still tells the caller to check
    // their email. That is harmless only while SignIn.RequireConfirmedEmail is false. The first
    // time it is turned on, every newly registered account is permanently unable to log in and
    // there is no path to mint a token for it.
    //
    // This guard does not finish the feature; it makes the trap impossible to walk into
    // silently. It disarms itself the moment the token is actually carried on the event, so
    // whoever completes the feature does not have to know this check exists.
    var identityOptions = app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
    if (identityOptions.SignIn.RequireConfirmedEmail && !EmailConfirmationTokenIsDelivered())
    {
        throw new InvalidOperationException(
            "Identity:RequireConfirmedEmail is enabled but registration does not deliver the " +
            "confirmation token: RegisterCommandHandler generates one and discards it, and " +
            $"{nameof(UserRegisteredIntegrationEvent)} carries no token property, so no " +
            "confirmation email can be sent and every new account would be permanently locked " +
            "out. Wire the token into the integration event before enabling this.");
    }

    // Apply database migrations automatically (Production/Development/Sandbox)
    // Skip for Testing environment (uses in-memory database)
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
                break;
            }
            catch (Exception ex) when (IsPostgresStartupException(ex) && attempt < maxMigrationAttempts)
            {
                Log.Warning(ex,
                    "Database is not ready yet (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}...",
                    attempt,
                    maxMigrationAttempts,
                    migrationDelay);

                // API-13. Honour shutdown: without a token this loop can hold a failing boot
                // open for up to 40s (8 attempts x linear backoff) after Ctrl+C or a
                // container stop, which reads as a hung process rather than a failed one.
                await Task.Delay(migrationDelay, app.Lifetime.ApplicationStopping);
                migrationDelay += TimeSpan.FromSeconds(5);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to apply database migrations or seed data");
                throw;
            }
        }
    }

    // Initialize telemetry
    var metrics = app.Services.GetRequiredService<IIdentityMetrics>();
    IdentityTelemetry.Initialize(metrics);

    // Global Exception Handler - must be first middleware
    app.UseGlobalExceptionHandler();

    if (forwardedHeadersEnabled)
    {
        app.UseForwardedHeaders();
    }

    // Uniform Response Timing - prevents account enumeration through timing attacks
    // Must come early in pipeline to measure total response time
    app.UseMiddleware<UniformResponseTimingMiddleware>();

    app.UseEShopRequestLogging();


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

    // HTTPS redirection must run BEFORE the rate limiter and CORS. It sat after both, so a
    // plain-HTTP request that was going to be redirected anyway still consumed a rate-limit
    // permit and still had CORS headers computed for it — work done on a request that never
    // reaches a handler, and a cheap way for an unauthenticated caller to spend another
    // client's allowance. Note the redirect is conditional on ASPNETCORE_HTTPS_PORT/HTTPS_PORT,
    // neither of which docker-compose sets, so **compose has no HTTPS redirect and no HSTS by
    // design** — TLS is expected to terminate in front of the stack.
    var httpsPort = app.Configuration["ASPNETCORE_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORT"];
    if (!string.IsNullOrWhiteSpace(httpsPort))
    {
        app.UseHttpsRedirection();
    }

    // Add Rate Limiting middleware
    app.UseRateLimiter();

    // Add CORS
    app.UseCors("AllowFrontend");

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
    // Both scrape endpoints are anonymous. Restricted to loopback + private networks unless
    // Metrics:AllowedNetworks says otherwise; Testing is exempt (TestServer has no socket).
    app.UseEShopMetricsAccess(app.Configuration, app.Environment);
    app.MapMetrics("/prometheus");
    // /metrics/otel — OpenTelemetry metrics (http.server.request.duration, process.runtime.*, etc.)
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
                // API-9. These advertised /auth/refresh and /auth/logout, neither of which
                // exists — AuthController maps refresh-token and revoke-token.
                refresh = "POST /api/v1/auth/refresh-token",
                revoke = "POST /api/v1/auth/revoke-token"
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

    // Rethrow so the process exits non-zero. Without this the host logs [FTL] and then reports
    // success, so a config-guard rejection or an unreachable broker looks like a clean shutdown
    // to anything checking exit status instead of parsing logs.
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// API-10. These local functions used to sit between two app.Use* calls, which broke the
// pipeline's top-to-bottom reading order — the one place in this file where order is the
// meaning. Top-level local functions are in scope for the whole file regardless of where they
// are declared, so the end is the right home.

static bool IsPostgresStartupException(Exception exception)
{
    if (exception is PostgresException { SqlState: "57P03" })
    {
        return true;
    }

    return exception.InnerException is not null
        && IsPostgresStartupException(exception.InnerException);
}

// Reflection rather than a constant so the BUG-03 rail disarms itself when the parked
// email-confirmation feature is finished, instead of becoming a stale flag someone has to
// remember to flip. Any property on the event whose name ends in "ConfirmationToken" counts.
static bool EmailConfirmationTokenIsDelivered() =>
    typeof(UserRegisteredIntegrationEvent)
        .GetProperties()
        .Any(p => p.Name.EndsWith("ConfirmationToken", StringComparison.OrdinalIgnoreCase));
