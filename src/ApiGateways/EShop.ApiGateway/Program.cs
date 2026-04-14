using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Health;
using EShop.ApiGateway.Middleware;
using EShop.ApiGateway.Notifications;
using EShop.ApiGateway.Simulation;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;
using Serilog.Events;

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
    .Enrich.WithProperty("Application", "EShop.ApiGateway"));

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.Configure<SimulationOptions>(builder.Configuration.GetSection(SimulationOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<EmailQueueHealthOptions>(builder.Configuration.GetSection(EmailQueueHealthOptions.SectionName));
builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection(RateLimitingOptions.SectionName));
builder.Services.Configure<IdentityServiceOptions>(builder.Configuration.GetSection(IdentityServiceOptions.SectionName));
builder.Services.Configure<IdentityProxyOptions>(builder.Configuration.GetSection(IdentityProxyOptions.SectionName));
builder.Services.Configure<CatalogProxyOptions>(builder.Configuration.GetSection(CatalogProxyOptions.SectionName));
builder.Services.Configure<OrderingProxyOptions>(builder.Configuration.GetSection(OrderingProxyOptions.SectionName));
builder.Services.Configure<BasketProxyOptions>(builder.Configuration.GetSection(BasketProxyOptions.SectionName));

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

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var jwtSecretKey = jwtSettings["SecretKey"];
if (string.IsNullOrWhiteSpace(jwtSecretKey) || jwtSecretKey.Length < 32)
{
    throw new InvalidOperationException("JwtSettings:SecretKey must be configured and at least 32 characters long.");
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
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        if (origins.Length == 0 &&
            (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")))
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
            return;
        }

        if (origins.Length == 0 &&
            !builder.Environment.IsDevelopment() &&
            !builder.Environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                $"Cors:AllowedOrigins is empty in {builder.Environment.EnvironmentName}. " +
                "Configure allowed origins before deploying to non-development environments.");
        }

        policy.WithOrigins(origins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
{
    var settings = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
        ?? new RateLimitingOptions();

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.GlobalPermitLimit,
                Window = TimeSpan.FromSeconds(settings.GlobalWindowSeconds),
                AutoReplenishment = true
            }));

    options.AddFixedWindowLimiter("simulation", limiterOptions =>
    {
        limiterOptions.PermitLimit = settings.SimulationPermitLimit;
        limiterOptions.Window = TimeSpan.FromSeconds(settings.SimulationWindowSeconds);
        limiterOptions.AutoReplenishment = true;
    });
});

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHttpClient();

builder.Services.AddEShopOpenTelemetry(
    builder.Configuration,
    serviceName: "EShop.ApiGateway",
    serviceVersion: "1.0.0",
    environment: builder.Environment,
    additionalSources: "EShop.ApiGateway");

builder.Services.AddSingleton<ISimulationProfileProvider, SimulationProfileProvider>();
builder.Services.AddSingleton<ISimulationResponseFactory, SimulationResponseFactory>();

builder.Services.AddSingleton<GatewayEmailQueue>();
builder.Services.AddSingleton<IEmailNotificationService, EmailNotificationService>();
builder.Services.AddSingleton<IEmailTemplateEngine, EmailTemplateEngine>();
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();
builder.Services.AddHttpClient<IAccountEmailResolver, IdentityAccountEmailResolver>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityServiceOptions>>().Value;

    if (!string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        client.BaseAddress = new Uri(options.BaseUrl);
    }

    if (!string.IsNullOrWhiteSpace(options.ApiKey) && !string.IsNullOrWhiteSpace(options.ApiKeyHeaderName))
    {
        client.DefaultRequestHeaders.Remove(options.ApiKeyHeaderName);
        client.DefaultRequestHeaders.Add(options.ApiKeyHeaderName, options.ApiKey);
    }

    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
});
builder.Services.AddHostedService<GatewayEmailDispatcher>();

builder.Services.AddHealthChecks()
    .AddCheck<DownstreamHealthCheck>("downstream", tags: ["ready"])
    .AddCheck<SmtpGatewayHealthCheck>("smtp", tags: ["ready"])
    .AddCheck<EmailQueueHealthCheck>("email-queue", tags: ["ready"])
    .AddCheck<GatewayLivenessHealthCheck>("gateway-liveness", tags: ["live"]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseGlobalExceptionHandler();

if (forwardedProxies.Length > 0)
{
    app.UseForwardedHeaders();
}

app.UseEShopRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<EmailTriggerMiddleware>();
app.UseCors("AllowFrontend");
app.UseRateLimiter();

var httpsPort = app.Configuration["ASPNETCORE_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORT"];
if (!string.IsNullOrWhiteSpace(httpsPort))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdentityProxyGuardMiddleware>();
app.UseMiddleware<CatalogProxyGuardMiddleware>();
app.UseMiddleware<OrderingProxyGuardMiddleware>();
app.UseMiddleware<BasketProxyGuardMiddleware>();

app.UseMiddleware<SimulationDecisionMiddleware>();
app.UseMiddleware<SimulationResponseMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapReverseProxy();

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
    service = "EShop API Gateway",
    version = "1.0.0",
    environment = app.Environment.EnvironmentName,
    endpoints = new
    {
        health = "/health",
        healthReady = "/health/ready",
        healthLive = "/health/live",
        metrics = new { prometheus = "/prometheus", otel = "/metrics" }
    }
}));

app.Run();

public partial class Program;
