using EShop.Basket.API.Endpoints;
using EShop.Basket.API.Infrastructure.Configuration;
using EShop.Basket.API.Infrastructure.HealthChecks;
using EShop.Basket.API.Infrastructure.Security;
using EShop.Basket.Application.Extensions;
using EShop.Basket.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.Http;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

// StackExchange.Redis's guidance for its "Timeout ... WORKER busy" failures: when a burst queues more work than the pool
// has threads, the pool adds threads slowly and Redis replies wait behind them. Every Redis call here is asynchronous, so
// this is headroom, not a fix for blocking code (Basket audit L8 — reviewed and kept; it had no comment saying why).
ThreadPool.SetMinThreads(workerThreads: 100, completionPortThreads: 100);

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
    .Enrich.WithProperty("Application", "EShop.Basket.API"));

// Shared across every service — reads KnownNetworks as well as KnownProxies, which is what
// works under Docker/Kubernetes, and logs rather than silently dropping an unparseable entry.
var forwardedHeadersEnabled = builder.Services.AddEShopForwardedHeaders(builder.Configuration);

// Basket audit S10 (M11): Redis must be configured, and outside Development and Testing no Redis or RabbitMQ setting may
// still be a placeholder. Before the messaging registration below, which accepts any non-empty RabbitMQ values.
BasketConfigurationGuard.Validate(builder.Configuration, builder.Environment);

// The pipeline is Validation -> Logging -> handler, both registered by AddBasketApplication. Basket has
// no TransactionBehavior (there is no database) and, since Basket audit S5 (D5), no caching behaviors:
// GetBasketQuery was cached as a second Redis copy of a Redis document, which cost the same round trip
// as the source and served old prices after a price sync. Don't add AddEShopCacheInvalidation() back.
builder.Services.AddBasketApplication();
builder.Services.AddBasketInfrastructure(builder.Configuration);
builder.Services.AddBasketMessaging(
    builder.Configuration,
    builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));

builder.Services.AddEShopOpenTelemetry(
    builder.Configuration,
    serviceName: "EShop.Basket.API",
    serviceVersion: "1.0.0",
    environment: builder.Environment,
    additionalSources: "EShop.Basket");

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings are required.");

// Basket audit S10 (M11): the shared guard. The hand-rolled check here tested length only, so the tracked Development
// placeholder (CHANGE_ME_..., longer than 32 characters) would have booted Basket in Sandbox or Production.
JwtSecretGuard.Validate(jwtSettings.SecretKey, builder.Environment);

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
    // The owner for everything, an admin for reads only (Basket audit S10, D10).
    options.AddPolicy(OwnerOrAdminReadRequirement.PolicyName, policy => policy.Requirements.Add(new OwnerOrAdminReadRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, OwnerOrAdminReadHandler>();

// Validated here rather than inside AddPolicy: CORS builds its policies lazily on first use,
// so a throw in the lambda is a request-time 500 on a host that already reported healthy.
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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partition on GetClientPartitionKey, not on RemoteIpAddress directly: the helper normalises
    // IPv4-mapped IPv6, so ::ffff:1.2.3.4 and 1.2.3.4 share one bucket instead of a dual-stack
    // client silently getting two allowances. Basket had the same mismatch Catalog did.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: EShopForwardedHeaders.GetClientPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddHealthChecks()
    // The application's own multiplexer (Basket audit L8): the connection-string overload opened a second connection
    // just to be checked, so the check could pass while the one the basket actually uses was broken.
    .AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis", tags: ["cache", "ready"])
    .AddCheck<BasketOutboxHealthCheck>("basket-outbox", tags: ["outbox", "ready"])
    .AddCheck<BasketReadinessHealthCheck>("basket-readiness", tags: ["ready"])
    .AddCheck<BasketLivenessHealthCheck>("basket-liveness", tags: ["live"]);

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Basket has never had a NotFoundException branch - its 404s come from Result errors, mapped by
// BasketEndpoints.StatusFor (Basket audit S8), not from exceptions. AddNotFound() is deliberately not registered.
builder.Services.AddEShopProblemDetails(options => options.AddCommon());

var app = builder.Build();

app.UseGlobalExceptionHandler();

// OpenAPI and Scalar UI: every environment except Production, the one rule all services share (Ordering
// audit L10, EShopApiDocs). This was Development only.
if (EShopApiDocs.IsExposedIn(app.Environment))
{
    app.MapOpenApi();

    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("EShop Basket API")
            .WithTheme(ScalarTheme.Purple)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
            .WithOpenApiRoutePattern("/openapi/{documentName}.json");
    });
}

app.UseEShopForwardedHeaders(forwardedHeadersEnabled);

app.UseEShopRequestLogging();

app.UseCors("AllowFrontend");
app.UseRateLimiter();

var httpsPort = app.Configuration["ASPNETCORE_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORT"];
if (!string.IsNullOrWhiteSpace(httpsPort))
{
    app.UseHttpsRedirection();
}

app.UseHttpMetrics(options =>
{
    options.AddCustomLabel("service", _ => "basket");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapBasketEndpoints();
app.MapBasketOutboxAdminEndpoints();

// Both scrape endpoints are anonymous. Restricted to loopback + private networks unless
// Metrics:AllowedNetworks says otherwise; Testing is exempt (TestServer has no socket).
app.UseEShopMetricsAccess(app.Configuration, app.Environment);
app.MapMetrics("/prometheus");
app.UseEShopOpenTelemetryPrometheus();

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

// Anonymous, so it names no environment and no routes (Basket audit S10, L10) — Payment's shape. The route map lives
// in the OpenAPI document, which Production does not expose.
app.MapGet("/", () => Results.Ok(new
{
    service = "EShop Basket API",
    version = "1.0.0",
    endpoints = new
    {
        healthReady = "/health/ready",
        healthLive = "/health/live",
        metrics = new { prometheus = "/prometheus", otel = "/metrics" }
    }
}));

app.Run();
