using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.HealthChecks;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryDatabase = false,
        string? inMemoryDatabaseName = null)
    {
        services.Configure<PaymentSimulationSettings>(
            configuration.GetSection(PaymentSimulationSettings.SectionName));

        services.Configure<CancelledOrderRefundSettings>(
            configuration.GetSection(CancelledOrderRefundSettings.SectionName));

        services.AddOptions<StripeSettings>()
            .Bind(configuration.GetSection(StripeSettings.SectionName))
            .Validate(static settings =>
                !settings.Enabled ||
                (!string.IsNullOrWhiteSpace(settings.SecretKey)
                 && settings.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal)
                 && (settings.SkipWebhookSignatureVerification || !string.IsNullOrWhiteSpace(settings.WebhookSecret))),
                "Stripe sandbox mode requires SecretKey with sk_test_ prefix and webhook secret unless signature verification is explicitly skipped.")
            .Validate(static settings =>
                !settings.Enabled ||
                string.IsNullOrWhiteSpace(settings.PublishableKey)
                || settings.PublishableKey.StartsWith("pk_test_", StringComparison.Ordinal),
                "Stripe publishable key must use pk_test_ prefix in sandbox mode.")
            .Validate(static settings =>
                !settings.AllowMissingSignatureHeaderInBypassMode || settings.SkipWebhookSignatureVerification,
                "AllowMissingSignatureHeaderInBypassMode requires SkipWebhookSignatureVerification to be enabled.")
            .ValidateOnStart();

        if (useInMemoryDatabase)
        {
            var dbName = inMemoryDatabaseName ?? $"PaymentTestDb_{Guid.NewGuid()}";
            services.AddDbContext<PaymentDbContext>(options => options.UseInMemoryDatabase(dbName));
        }
        else
        {
            services.AddDbContext<PaymentDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("PaymentDb")));
        }

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<PaymentDbContext>());
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<PaymentDbContext>());

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentProcessor, MockPaymentProcessor>();
        services.AddScoped<IStripeCustomerService, StripeCustomerService>();
        services.AddScoped<IStripePaymentService, StripePaymentService>();
        services.AddScoped<IStripeWebhookProcessor, StripeWebhookProcessor>();

        // Ordering audit Stage 10. The DbContext above was registered "for the outbox processor", but
        // the processor itself never was, so every event Payment enqueued (PaymentSuccess, Failed,
        // Created, Completed, Refunded) was written to outbox_messages and never sent: Ordering never
        // learned that a payment succeeded. Rows left over from that time are discarded by the
        // DiscardUndispatchedOutboxBacklog migration, which runs before these services start.
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
            .AddCheck<OutboxHealthCheck>(
                "outbox",
                tags: ["ready", "outbox"]);

        return services;
    }

    public static IServiceCollection AddPaymentMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.AddMessaging<PaymentDbContext>(
            configuration,
            "payment",
            isDevelopment,
            bus =>
            {
                bus.AddConsumer<OrderCreatedConsumer>();
                bus.AddConsumer<OrderCancelledConsumer>();
            });

        return services;
    }
}
