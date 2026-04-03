using EShop.Basket.API.Endpoints;
using EShop.Basket.API.Infrastructure.Configuration;
using EShop.Basket.API.Infrastructure.HealthChecks;
using EShop.Basket.API.Infrastructure.Middleware;
using EShop.Basket.API.Infrastructure.Security;
using EShop.Basket.Application.Extensions;
using EShop.Basket.Infrastructure.Caching;
using EShop.Basket.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.Extensions;
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

var forwardedProxies = builder.Configuration
    .GetSection("ForwardedHeaders:KnownProxies")
    .Get<string[]>() ?? [];

if (forwardedProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
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
    options.Configuration = redisConnectionString;
    options.InstanceName = "EShop_Basket_";
    options.ConfigurationOptions = ConfigurationOptions.Parse(redisConnectionString);
    options.ConfigurationOptions.AbortOnConnectFail = false;
    options.ConfigurationOptions.ConnectTimeout = 5000;
    options.ConfigurationOptions.SyncTimeout = 5000;
    options.ConfigurationOptions.ConnectRetry = 3;
    options.ConfigurationOptions.KeepAlive = 60;
    options.ConfigurationOptions.ReconnectRetryPolicy = new LinearRetry(5000);
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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
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

var app = builder.Build();

app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
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

if (forwardedProxies.Length > 0)
{
    app.UseForwardedHeaders();
}

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
});

app.UseCors("AllowFrontend");
app.UseRateLimiter();

app.UseHttpsRedirection();

app.UseHttpMetrics(options =>
{
    options.AddCustomLabel("service", _ => "basket");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapBasketEndpoints();

app.MapMetrics("/prometheus");
app.UseEShopOpenTelemetryPrometheus();

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
        return context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { status = report.Status.ToString() }));
    }
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
