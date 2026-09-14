using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Infrastructure.BackgroundServices;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.HealthChecks;
using EShop.Notification.Infrastructure.Repositories;
using EShop.Notification.Infrastructure.Services;
using MassTransit;
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
        services.Configure<PasswordResetSettings>(configuration.GetSection(PasswordResetSettings.SectionName));
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

        // The consumers date their delivery attempts with it (Notification audit D5).
        services.TryAddSingleton(TimeProvider.System);

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

        // Notification audit S6 (debt 4, L11, D9). Notification publishes nothing, so it runs no outbox processor, no
        // outbox cleanup (processed_messages has not been written here since S2) and no outbox health check. The two
        // tables stay in the schema because BaseDbContext maps them; they are empty.

        // Notification audit S5 (M8, D8): NotificationLogs rows are deleted 90 days after their last update.
        services.Configure<NotificationLogRetentionSettings>(
            configuration.GetSection(NotificationLogRetentionSettings.SectionName));
        services.AddHostedService<NotificationLogRetentionService>();

        // Readiness is what the consumers need: this database, and RabbitMQ (registered by AddEShopBus). SMTP is on
        // /health only (S6, M11): an outage there is recorded per message and retried, and readiness gates no traffic here.
        services.AddHealthChecks()
            .AddCheck<NotificationDbHealthCheck>("notification-db", tags: ["db", "ready"])
            .AddCheck<SmtpHealthCheck>("smtp", tags: ["smtp"])
            .AddCheck<NotificationLivenessHealthCheck>("notification-liveness", tags: ["live"]);

        return services;
    }

    public static IServiceCollection AddNotificationMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        // The bus alone (Notification audit S6, debt 4): AddMessaging would also register an integration event outbox,
        // and Notification publishes nothing.
        services.AddEShopBus(configuration, "notification", isDevelopment, bus => bus.AddNotificationConsumers());

        return services;
    }

    /// <summary>
    /// The seven consumers, with the password-reset endpoint's definition (Notification audit D6). Public so the tests
    /// register exactly what production does.
    /// </summary>
    public static void AddNotificationConsumers(this IBusRegistrationConfigurator bus)
    {
        bus.AddConsumer<OrderCreatedConsumer>();
        bus.AddConsumer<OrderShippedConsumer>();
        bus.AddConsumer<PaymentCreatedConsumer>();
        bus.AddConsumer<PaymentCompletedConsumer>();
        bus.AddConsumer<PaymentFailedConsumer>();
        bus.AddConsumer<PaymentRefundedConsumer>();
        bus.AddConsumer<PasswordResetRequestedConsumer, PasswordResetRequestedConsumerDefinition>();
    }
}
