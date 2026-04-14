using EShop.Payment.API.Endpoints;
using EShop.Payment.API.Infrastructure.Configuration;
using EShop.Payment.API.Infrastructure.Middleware;
using EShop.Payment.API.Infrastructure.Security;
using EShop.Payment.Application.Extensions;
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

if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey) || jwtSettings.SecretKey.Length < 32)
{
    throw new InvalidOperationException("JWT SecretKey must be configured and at least 32 characters long.");
}

if (IsProductionLikeEnvironment(builder.Environment))
{
    EnsureNoPlaceholderValue(jwtSettings.SecretKey, "JwtSettings:SecretKey", builder.Environment.EnvironmentName);

    var paymentDbConnectionString = builder.Configuration.GetConnectionString("PaymentDb");
    EnsureNoPlaceholderValue(paymentDbConnectionString, "ConnectionStrings:PaymentDb", builder.Environment.EnvironmentName);

    if (paymentDbConnectionString!.Contains("localhost", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"ConnectionStrings:PaymentDb contains localhost in {builder.Environment.EnvironmentName}. Use managed environment-specific connection configuration.");
    }
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

builder.Services.AddSingleton<IAuthorizationHandler, SameUserOrAdminHandler>();

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var httpsPort = app.Configuration["ASPNETCORE_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORT"];
if (!string.IsNullOrWhiteSpace(httpsPort))
{
    app.UseHttpsRedirection();
}

app.UseGlobalExceptionHandler();
app.UseEShopRequestLogging();
app.UseCors("AllowFrontend");

app.UseHttpMetrics(options =>
{
    options.AddCustomLabel("service", _ => "payment");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapPaymentEndpoints();

// /prometheus — custom prometheus-net metrics
app.MapMetrics("/prometheus");
// /metrics — OpenTelemetry metrics endpoint
app.UseEShopOpenTelemetryPrometheus();

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapGet("/", () => Results.Ok(new
{
    service = "EShop Payment API",
    version = "1.0.0",
    environment = app.Environment.EnvironmentName,
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

static bool IsProductionLikeEnvironment(IHostEnvironment environment)
{
    return !environment.IsDevelopment()
        && !environment.IsEnvironment("Testing")
        && !environment.IsEnvironment("Sandbox");
}

static void EnsureNoPlaceholderValue(string? value, string settingName, string environmentName)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"{settingName} is required in {environmentName}.");
    }

    var placeholderPatterns = new[] { "CHANGE_ME", "LOCAL_", "#{", "REPLACE_WITH_", "YOUR_", "placeholder" };

    foreach (var pattern in placeholderPatterns)
    {
        if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{settingName} contains placeholder pattern '{pattern}' in {environmentName}. Replace it with a secure value.");
        }
    }
}

public partial class Program;
