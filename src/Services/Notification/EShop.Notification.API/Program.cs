using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.Notification.Application.Extensions;
using EShop.Notification.API.Endpoints;
using EShop.Notification.Infrastructure.Extensions;
using EShop.Notification.API.Configuration;
using EShop.Notification.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using HealthChecks.UI.Client;
using Npgsql;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using System.Text;
using System.Threading.RateLimiting;
using CorsOriginGuard = EShop.BuildingBlocks.Infrastructure.Configuration.CorsOriginGuard;

ThreadPool.SetMinThreads(workerThreads: 50, completionPortThreads: 50);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.Local.json",
        optional: true,
        reloadOnChange: true);

    // Notification audit S4 (M9, M10, L6, L23; D4). Before anything is registered, so a misconfigured deploy never
    // starts; the consumers used to find out one message at a time.
    NotificationConfigurationGuard.Validate(builder.Configuration, builder.Environment);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "EShop.Notification.API"));

    // Shared across every service — reads KnownNetworks as well as KnownProxies, which is what works under
    // Docker/Kubernetes, and logs rather than silently dropping an unparseable entry. Since Admin panel S12 this is a
    // live exposure rather than consistency alone: the journal endpoints sit behind the gateway, and the rate limiter
    // below partitions on the address this rewrites.
    var forwardedHeadersEnabled = builder.Services.AddEShopForwardedHeaders(builder.Configuration);

    // Admin audit trail (S15, Q8a). FIRST, so AuditBehavior is the outermost behavior: outside the transaction,
    // recording the outcome the caller got. Registered after the Application call it runs inside TransactionBehavior.
    builder.Services.AddEShopAuditLog<NotificationDbContext>("notification");

    builder.Services.AddNotificationApplication();

    var useInMemoryDb = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddNotificationInfrastructure(
        builder.Configuration,
        useInMemoryDatabase: useInMemoryDb);

    builder.Services.AddNotificationMessaging(
        builder.Configuration,
        builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));

    builder.Services.AddEShopOpenTelemetry(
        builder.Configuration,
        serviceName: "EShop.Notification.API",
        serviceVersion: "1.0.0",
        environment: builder.Environment,
        additionalSources: "EShop.Notification");

    // ---------------------------------------------------------------------------------------------------------
    // Admin panel S12 (endpoints #70, #71, #77). Notification's first web surface: JWT bearer authentication,
    // permission policies, CORS, a partitioned rate limiter and the OpenAPI document. NotificationConfigurationGuard
    // has already refused a missing, short or placeholder signing key and an empty issuer or audience, so binding
    // below cannot produce a host that rejects every token while reporting healthy.
    // ---------------------------------------------------------------------------------------------------------
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
        ?? throw new InvalidOperationException("JWT settings are required.");

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

    // Decision Q4c: one policy per permission, resolved from the caller's roles through RolePermissionBundles.
    // Notification deliberately declares NO "Admin" policy — it has never had one, and the root guide's rule is to
    // reach for a permission exactly here. The journal endpoints require notifications.read; an Admin token carries
    // every permission through the bundle, so nothing has to be granted for an existing administrator to use them.
    builder.Services.AddEShopPermissions();

    // Validated while the host is composed rather than inside the AddPolicy lambda, which CORS builds lazily on first
    // use: a misconfigured deploy would otherwise start healthy and throw on its first cross-origin request.
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

    // Payment's limiter, ported as-is: partitioned per client (never AddFixedWindowLimiter(name, ...), which builds
    // ONE bucket shared by every caller), off under Testing unless RateLimiting:EnableInTesting, and read from the
    // same RateLimiting:* keys as Catalog, Identity, Ordering and Payment.
    var rateLimitingEnabled = !builder.Environment.IsEnvironment("Testing")
        || builder.Configuration.GetValue<bool>("RateLimiting:EnableInTesting");
    var globalPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 100;
    var globalWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Global:WindowSeconds") ?? 60;

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        if (rateLimitingEnabled)
        {
            // GetClientPartitionKey normalises IPv4-mapped IPv6, so a dual-stack client cannot claim two allowances,
            // and it reads the address UseForwardedHeaders has already rewritten.
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
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();

    // Still no AddNotFound(): Notification throws no NotFoundException — every 404 here is a Result error mapped by the
    // endpoint, as Basket's are.
    //
    // Admin panel S13 added the two branches S12 deliberately left out, because S13 is what made them reachable:
    // - AddEfConcurrency(): mark-undeliverable saves a tracked row under its xmin row version, so a delivery that claims
    //   the row between the operator's read and save raises DbUpdateConcurrencyException — a 409, not a 500. Still no
    //   AddEfDuplicateKey(): the one unique index (EventId) is written only by the consumers, which handle it themselves.
    // - AddMalformedJsonBody() with ThrowOnBadRequest: the actions take JSON bodies. Without ThrowOnBadRequest a
    //   malformed one is a bare 400 with an empty body outside Development; throwing routes it to the branch, which
    //   answers problem+json naming the offending JSON path — Ordering's pairing.
    builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
    builder.Services.AddEShopProblemDetails(options => options
        .AddCommon()
        .AddEfConcurrency()
        .AddMalformedJsonBody());

    var app = builder.Build();

    if (!useInMemoryDb)
    {
        const int maxMigrationAttempts = 8;
        var migrationDelay = TimeSpan.FromSeconds(5);

        for (var attempt = 1; attempt <= maxMigrationAttempts; attempt++)
        {
            try
            {
                Log.Information("Applying notification database migrations...");
                using var scope = app.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

                await dbContext.Database.MigrateAsync();

                Log.Information("Notification database migrations applied successfully");
                break;
            }
            catch (Exception ex) when (IsPostgresStartupException(ex) && attempt < maxMigrationAttempts)
            {
                Log.Warning(ex,
                    "Notification database is not ready yet (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}...",
                    attempt,
                    maxMigrationAttempts,
                    migrationDelay);

                await Task.Delay(migrationDelay);
                migrationDelay += TimeSpan.FromSeconds(5);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to apply notification database migrations");
                throw;
            }
        }
    }

    // Admin panel S12. Notification's endpoints can now fail, so the shared RFC 7807 middleware has something to do.
    // It is registered first, as in every other component, so it wraps everything below it.
    app.UseGlobalExceptionHandler();

    // OpenAPI and Scalar: every environment except Production, the one rule all services share (Ordering audit L10,
    // EShopApiDocs). Notification is the seventh and last component to map them — it had no API to document.
    if (EShopApiDocs.IsExposedIn(app.Environment))
    {
        app.MapOpenApi();

        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("EShop Notification API")
                .WithTheme(ScalarTheme.Purple)
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                .WithOpenApiRoutePattern("/openapi/{documentName}.json");
        });
    }

    // Before anything that reads the client address: the rate limiter partitions on it.
    app.UseEShopForwardedHeaders(forwardedHeadersEnabled);

    app.UseEShopRequestLogging();

    app.UseCors("AllowFrontend");

    // After CORS, so a preflight does not spend a permit; before authentication and the endpoints.
    app.UseRateLimiter();

    app.UseHttpMetrics(options =>
    {
        options.AddCustomLabel("service", _ => "notification");
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapNotificationEndpoints();
    // This service's slice of the admin audit trail (S15); the gateway serves the merged view on the same path.
    app.MapEShopAuditLog();

    // Notification previously had no /health, and its liveness predicate fell back to
    // `Tags.Count == 0` because nothing was tagged "live" — it matched no check and so always
    // reported Healthy. NotificationLivenessHealthCheck now gives it something real to report.
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

    // Both scrape endpoints are anonymous. Restricted to loopback + private networks unless
    // Metrics:AllowedNetworks says otherwise; Testing is exempt (TestServer has no socket).
    app.UseEShopMetricsAccess(app.Configuration, app.Environment);
    app.MapMetrics("/prometheus");
    app.UseEShopOpenTelemetryPrometheus();

    // Notification audit S4 (L8): Basket's shape. An anonymous endpoint names no environment.
    app.MapGet("/", () => Results.Ok(new
    {
        service = "EShop Notification API",
        version = "1.0.0",
        endpoints = new
        {
            healthReady = "/health/ready",
            healthLive = "/health/live",
            metrics = new { prometheus = "/prometheus", otel = "/metrics" }
        }
    }));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Notification Service terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
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

public partial class Program;
