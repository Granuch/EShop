using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.Notification.Application.Extensions;
using EShop.Notification.Infrastructure.Extensions;
using EShop.Notification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using HealthChecks.UI.Client;
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

    var app = builder.Build();

    if (!useInMemoryDb)
    {
        try
        {
            Log.Information("Applying notification database migrations...");
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

            await dbContext.Database.MigrateAsync();

            Log.Information("Notification database migrations applied successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to apply notification database migrations");
            throw;
        }
    }

    app.UseEShopRequestLogging();

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

    app.MapGet("/", () => Results.Ok(new
    {
        service = "EShop Notification API",
        version = "1.0.0",
        environment = app.Environment.EnvironmentName,
        endpoints = new
        {
            healthReady = "/health/ready",
            healthLive = "/health/live"
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

public partial class Program;
