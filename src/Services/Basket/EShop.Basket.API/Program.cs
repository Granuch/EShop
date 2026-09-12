using EShop.Basket.API.Endpoints;
using EShop.Basket.API.Infrastructure.Configuration;
using EShop.Basket.API.Infrastructure.HealthChecks;
using EShop.Basket.API.Infrastructure.Security;
using EShop.Basket.Application.Extensions;
using EShop.Basket.Infrastructure.Caching;
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
using Microsoft.Extensions.Caching.StackExchangeRedis;
using StackExchange.Redis;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

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

// CacheInvalidation FIRST, then Application, then Infrastructure. MediatR runs pipeline behaviors
// in DI registration order (first registered = outermost), so these three calls are what sets the
// pipeline: CacheInvalidation -> Transaction -> Validation -> Logging -> Caching -> handler.
// CacheInvalidationBehavior has to be outermost because it invalidates AFTER the handler returns:
// registered inside TransactionBehavior it drained keys before the write committed, so a
// concurrent read could repopulate the cache with pre-commit data for the full TTL. Silent if
// broken — nothing fails, and it is invisible without reading all three extension methods.
builder.Services.AddEShopCacheInvalidation();
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

var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Redis connection string is required.");

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.InstanceName = "EShop_Basket_";
});

builder.Services.AddOptions<RedisCacheOptions>()
    .Configure<IConnectionMultiplexer>((options, mux) =>
    {
        options.ConnectionMultiplexerFactory = () => Task.FromResult(mux);
    });

builder.Services.AddCircuitBreakingCache(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(30));

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings are required.");

if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey) || jwtSettings.SecretKey.Length < 32)
{
    throw new InvalidOperationException("JWT SecretKey must be configured and at least 32 characters long.");
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("SameUserOrAdmin", policy => policy.Requirements.Add(new SameUserOrAdminRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, SameUserOrAdminHandler>();

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
    .AddRedis(redisConnectionString, name: "redis", tags: ["cache", "ready"])
    .AddCheck<BasketOutboxHealthCheck>("basket-outbox", tags: ["outbox", "ready"])
    .AddCheck<BasketReadinessHealthCheck>("basket-readiness", tags: ["ready"])
    .AddCheck<BasketLivenessHealthCheck>("basket-liveness", tags: ["live"]);

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Basket has never had a NotFoundException branch - its 404s come from the endpoints'
// ProblemFromError helper, not from exceptions. AddNotFound() is deliberately not registered.
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

app.MapGet("/", () => Results.Ok(new
{
    service = "EShop Basket API",
    version = "1.0.0",
    environment = app.Environment.EnvironmentName,
    endpoints = new
    {
        health = "/health",
        healthReady = "/health/ready",
        healthLive = "/health/live",
        metrics = new { prometheus = "/prometheus", otel = "/metrics" },
        basket = new
        {
            get = "GET /api/v1/basket/{userId}",
            addItem = "POST /api/v1/basket/{userId}/items",
            updateItem = "PUT /api/v1/basket/{userId}/items/{productId}",
            removeItem = "DELETE /api/v1/basket/{userId}/items/{productId}",
            clear = "DELETE /api/v1/basket/{userId}",
            checkout = "POST /api/v1/basket/{userId}/checkout"
        }
    }
}));

app.Run();
