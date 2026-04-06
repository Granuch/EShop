using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.HealthChecks;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.HealthChecks;
using EShop.Notification.Infrastructure.Repositories;
using EShop.Notification.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.Notification.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryDatabase = false,
        string? inMemoryDatabaseName = null)
    {
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();

        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
        services.Configure<IdentityServiceSettings>(configuration.GetSection(IdentityServiceSettings.SectionName));
        services.Configure<RabbitMqSettings>(configuration.GetSection(RabbitMqSettings.SectionName));

        if (useInMemoryDatabase)
        {
            var dbName = inMemoryDatabaseName ?? $"NotificationTestDb_{Guid.NewGuid()}";
            services.AddDbContext<NotificationDbContext>(options => options.UseInMemoryDatabase(dbName));
        }
        else
        {
            services.AddDbContext<NotificationDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("NotificationDb")));
        }

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<NotificationDbContext>());
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<NotificationDbContext>());

        services.AddScoped<IEmailService, EmailService>();
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();
        services.AddScoped<INotificationLogRepository, NotificationLogRepository>();

        services.AddHttpClient<IUserContactResolver, UserContactResolver>((sp, client) =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityServiceSettings>>().Value;

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                client.BaseAddress = new Uri(settings.BaseUrl);
            }

            if (!string.IsNullOrWhiteSpace(settings.ApiKey)
                && !string.IsNullOrWhiteSpace(settings.ApiKeyHeaderName))
            {
                client.DefaultRequestHeaders.Remove(settings.ApiKeyHeaderName);
                client.DefaultRequestHeaders.Add(settings.ApiKeyHeaderName, settings.ApiKey);
            }

            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        });

        services.AddSingleton(new OutboxProcessorOptions
        {
            BatchSize = 20,
            PollingIntervalMs = 1000,
            MaxRetries = 5,
            ErrorRetryDelayMs = 5000
        });
        services.AddHostedService<OutboxProcessorService>();

        services.AddSingleton(new OutboxCleanupOptions
        {
            RetentionDays = 7,
            CleanupIntervalHours = 6
        });
        services.AddHostedService<OutboxCleanupService>();

        services.AddSingleton(new OutboxHealthCheckOptions
        {
            DeadLetterWarningThreshold = 10,
            PendingWarningThreshold = 100
        });

        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck>("outbox", tags: ["ready", "outbox"])
            .AddCheck<NotificationDbHealthCheck>("notification-db", tags: ["db", "ready"])
            .AddCheck<SmtpHealthCheck>("smtp", tags: ["smtp", "ready"]);

        return services;
    }

    public static IServiceCollection AddNotificationMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.AddMessaging<NotificationDbContext>(
            configuration,
            isDevelopment,
            bus =>
            {
                bus.AddConsumer<OrderCreatedConsumer>();
                bus.AddConsumer<OrderShippedConsumer>();
                bus.AddConsumer<PaymentCreatedConsumer>();
                bus.AddConsumer<PaymentCompletedConsumer>();
                bus.AddConsumer<PaymentFailedConsumer>();
                bus.AddConsumer<PaymentRefundedConsumer>();
            });

        return services;
    }
}
