using EShop.Payment.API.Endpoints;
using EShop.Payment.API.Infrastructure.Configuration;
using EShop.Payment.API.Infrastructure.HealthChecks;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Payment.API.Infrastructure.Security;
using EShop.Payment.Application.Extensions;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Extensions;
using EShop.Payment.Infrastructure.Configuration;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Prometheus;
using Serilog;
using Serilog.Events;
using System.Text;
using System.Threading.RateLimiting;
using CorsOriginGuard = EShop.BuildingBlocks.Infrastructure.Configuration.CorsOriginGuard;
using JwtSecretGuard = EShop.BuildingBlocks.Infrastructure.Configuration.JwtSecretGuard;

ThreadPool.SetMinThreads(workerThreads: 50, completionPortThreads: 50);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.Local.json",
    optional: true,
    reloadOnChange: true);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("Application", "EShop.Payment.API"));

var useInMemoryDb = builder.Environment.IsEnvironment("Testing");

var startupStripeSettings = builder.Configuration
    .GetSection(StripeSettings.SectionName)
    .Get<StripeSettings>() ?? new StripeSettings();

if (startupStripeSettings.SkipWebhookSignatureVerification
    && !builder.Environment.IsDevelopment()
    && !builder.Environment.IsEnvironment("Sandbox")
    && !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException(
        "Stripe webhook signature verification bypass is only allowed in Development, Sandbox, or Testing environments.");
}

if (startupStripeSettings.SkipWebhookSignatureVerification
    && builder.Environment.IsEnvironment("Sandbox"))
{
    Log.Warning(
        "Stripe webhook signature verification is disabled in Sandbox by design for integration testing. Never enable this bypass outside Development/Sandbox/Testing.");
}

// Payment previously had no forwarded-headers handling at all, so behind the gateway every
// request appeared to come from the gateway address. Shared helper — see EShopForwardedHeaders.
var forwardedHeadersEnabled = builder.Services.AddEShopForwardedHeaders(builder.Configuration);

// Admin audit trail (S15, Q8a). FIRST, so AuditBehavior is the outermost behavior: outside the transaction,
// recording the outcome the caller got. Registered after the Application call it runs inside TransactionBehavior.
builder.Services.AddEShopAuditLog<PaymentDbContext>("payment");

builder.Services.AddPaymentApplication();
builder.Services.AddPaymentInfrastructure(builder.Configuration, useInMemoryDatabase: useInMemoryDb);
builder.Services.AddPaymentMessaging(
    builder.Configuration,
    builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));

builder.Services.AddEShopOpenTelemetry(
    builder.Configuration,
    serviceName: "EShop.Payment.API",
    serviceVersion: "1.0.0",
    environment: builder.Environment,
    additionalSources: "EShop.Payment");

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings are required.");

// Payment audit Stage 11 (M8). The shared guard, as Ordering uses, with Sandbox checked like any deployed environment.
// Payment's own check used to exempt Sandbox (its IsProductionLikeEnvironment excluded it). So a placeholder key booted
// payment-api and crash-looped Identity on the same shared key. The one Sandbox exemption left is the Stripe webhook
// bypass above, which is deliberate. Configuration/StartupGuardTests boots this file as Production and as Sandbox.
JwtSecretGuard.Validate(jwtSettings.SecretKey, builder.Environment);

if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    EnsureDeployableConnectionString(builder.Configuration.GetConnectionString("PaymentDb"), builder.Environment.EnvironmentName);
}

builder.Services.AddHttpContextAccessor();

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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("SameUserOrAdmin", policy =>
        policy.Requirements.Add(new SameUserOrAdminRequirement()));
});

// Decision Q4c: one policy per permission, resolved from the caller's roles through
// RolePermissionBundles. Additive — every existing role-based policy above is untouched, and
// the Admin role bundles every permission, so no existing caller loses access.
builder.Services.AddEShopPermissions();

builder.Services.AddSingleton<IAuthorizationHandler, SameUserOrAdminHandler>();

// Payment audit Stage 11 (M8). The shared CorsOriginGuard, run while the host is composed. The old check sat inside the
// AddPolicy lambda, which CORS builds lazily, so a misconfigured deploy started healthy and threw on its first
// cross-origin request. It also passed a placeholder origin, since it checked only that the list was not empty.
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

// Payment audit Stage 3 (H4). Payment had no rate limiter, so anything reaching payment-api directly could call
// /create-intent without limit, and each call creates a Stripe customer and intent. This is Ordering's global
// limiter (itself Catalog's), ported as-is: partitioned per client (never AddFixedWindowLimiter(name, ...), which
// is one bucket shared by every caller), off under Testing unless RateLimiting:EnableInTesting, and read from the
// same RateLimiting:* keys as Catalog, Identity and Ordering. The Stripe webhook opts out (see PaymentEndpoints).
var rateLimitingEnabled = !builder.Environment.IsEnvironment("Testing")
    || builder.Configuration.GetValue<bool>("RateLimiting:EnableInTesting");
var globalPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 100;
var globalWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Global:WindowSeconds") ?? 60;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    if (rateLimitingEnabled)
    {
        // GetClientPartitionKey normalises IPv4-mapped IPv6, so a dual-stack client cannot claim two
        // allowances, and it reads the address UseForwardedHeaders has already rewritten.
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

// Infrastructure calls AddHealthChecks() but registered no checks, so both health endpoints
// evaluated an empty set. The readiness check must be skipped under the in-memory provider,
// same as every other service's DB-backed check.
var paymentHealthChecks = builder.Services.AddHealthChecks()
    .AddCheck<PaymentLivenessHealthCheck>("payment-liveness", tags: ["live"]);

if (!useInMemoryDb)
{
    paymentHealthChecks.AddCheck<PaymentReadinessHealthCheck>("payment-readiness", tags: ["db", "ready"]);
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Payment audit Stage 10 (M6). Only a real conflict is a 409: a lost row-version race, or a unique index. Payment used to
// map every DbUpdateException to 409, so a value too long for its column told the client to retry a request that could
// never succeed. Any other persistence failure is now the generic 500, and is logged as one.
builder.Services.AddEShopProblemDetails(options => options
    .AddCommon()
    .AddNotFound()
    .AddEfConcurrency()
    .AddEfDuplicateKey());

var app = builder.Build();

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
            var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            await dbContext.Database.MigrateAsync();
            Log.Information("Database migrations applied successfully");
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

// OpenAPI: every environment except Production, the one rule all services share (Ordering audit L10,
// EShopApiDocs). This was Development only.
if (EShopApiDocs.IsExposedIn(app.Environment))
{
    app.MapOpenApi();
}

// Before anything that reads the client address or scheme, including HTTPS redirection.
app.UseEShopForwardedHeaders(forwardedHeadersEnabled);

var httpsPort = app.Configuration["ASPNETCORE_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORT"];
if (!string.IsNullOrWhiteSpace(httpsPort))
{
    app.UseHttpsRedirection();
}

app.UseGlobalExceptionHandler();
app.UseEShopRequestLogging();
app.UseCors("AllowFrontend");

// Payment audit Stage 3. Ordering's position: after HTTPS redirection and CORS, so a request that is only going to
// be redirected, or a preflight, does not spend a permit; before authentication and the endpoints.
app.UseRateLimiter();

app.UseHttpMetrics(options =>
{
    options.AddCustomLabel("service", _ => "payment");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapPaymentEndpoints();
// Payment's slices of the System page's read-only settings and feature flags (S19, #85/#89).
app.MapSystemEndpoints();
// This service's slice of the admin audit trail (S15); the gateway serves the merged view on the same path.
app.MapEShopAuditLog();

// /prometheus — custom prometheus-net metrics
// Both scrape endpoints are anonymous. Restricted to loopback + private networks unless
// Metrics:AllowedNetworks says otherwise; Testing is exempt (TestServer has no socket).
app.UseEShopMetricsAccess(app.Configuration, app.Environment);
app.MapMetrics("/prometheus");
// /metrics — OpenTelemetry metrics endpoint
app.UseEShopOpenTelemetryPrometheus();

// Payment previously had no /health at all, a readiness predicate of `_ => true` (which ran
// every check regardless of tag) and a liveness predicate of `_ => false` (which ran none and
// so could never report anything but Healthy). All three endpoints now match the shape the
// other six components use.
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

// Payment audit Stage 12 (D17). Anonymous, so it does not say which environment answered.
app.MapGet("/", () => Results.Ok(new
{
    service = "EShop Payment API",
    version = "1.0.0",
    endpoints = new
    {
        healthReady = "/health/ready",
        healthLive = "/health/live",
        metrics = new { prometheus = "/prometheus", otel = "/metrics" }
    }
}));

app.Run();

static bool IsPostgresStartupException(Exception exception)
{
    if (exception is PostgresException { SqlState: "57P03" })
    {
        return true;
    }

    return exception.InnerException is not null
        && IsPostgresStartupException(exception.InnerException);
}

// Payment audit Stage 11 (M8). The connection string a deployed Payment may use. It must be present and free of the repo's
// placeholder patterns (the list JwtSecretGuard checks). It must not be localhost, which inside a container is the
// container itself.
static void EnsureDeployableConnectionString(string? connectionString, string environmentName)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException($"ConnectionStrings:PaymentDb is required in {environmentName}.");
    }

    foreach (var pattern in JwtSecretGuard.PlaceholderPatterns)
    {
        if (connectionString.Contains(pattern, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:PaymentDb contains placeholder pattern '{pattern}' in {environmentName}. Replace it with a secure value.");
        }
    }

    if (connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"ConnectionStrings:PaymentDb contains localhost in {environmentName}. Use managed environment-specific connection configuration.");
    }
}

public partial class Program;
