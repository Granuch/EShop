using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.Notification.Application.Extensions;
using EShop.Notification.Infrastructure.Extensions;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using HealthChecks.UI.Client;
using Npgsql;
using Prometheus;
using Serilog;
using Serilog.Events;

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

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "EShop.Notification.API"));

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

    var passwordResetSettings = builder.Configuration
        .GetSection(PasswordResetSettings.SectionName)
        .Get<PasswordResetSettings>() ?? new PasswordResetSettings();

    if (!Uri.TryCreate(passwordResetSettings.ResetUrlBase, UriKind.Absolute, out var resetUri))
    {
        throw new InvalidOperationException("PasswordReset:ResetUrlBase must be configured as an absolute URL.");
    }

    if (!builder.Environment.IsDevelopment()
        && !builder.Environment.IsEnvironment("Testing")
        && !string.Equals(resetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"PasswordReset:ResetUrlBase must use HTTPS in {builder.Environment.EnvironmentName}.");
    }

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

    app.UseEShopRequestLogging();

    app.UseHttpMetrics(options =>
    {
        options.AddCustomLabel("service", _ => "notification");
    });

    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live") || check.Tags.Count == 0,
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapMetrics("/prometheus");
    app.UseEShopOpenTelemetryPrometheus();

    app.MapGet("/", () => Results.Ok(new
    {
        service = "EShop Notification API",
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
