using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.HealthChecks;
using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.QueryServices;
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

        // Payment audit Stage 9 (M4). One Stripe client for the process, built from Stripe:SecretKey and injected into
        // both Stripe services. StripePaymentService used to write the key into Stripe.net's process-wide
        // StripeConfiguration from its constructor. So StripeCustomerService, which used the process-wide client, only had
        // a key if a StripePaymentService had been built earlier in the same process.
        services.AddSingleton<Stripe.IStripeClient>(provider =>
            CreateStripeClient(provider.GetRequiredService<IOptions<StripeSettings>>().Value));

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
        services.AddScoped<IPaymentQueryService, PaymentQueryService>();
        services.AddScoped<IPaymentProcessor, MockPaymentProcessor>();
        services.AddScoped<IStripeCustomerService, StripeCustomerService>();
        services.AddScoped<IStripePaymentService, StripePaymentService>();
        services.AddScoped<IStripeWebhookEventParser, StripeWebhookEventParser>();
        services.AddScoped<IStripeWebhookProcessor, StripeWebhookProcessor>();

        // Admin panel S11 (endpoint #67). Singletons on purpose: both work through IServiceScopeFactory, because each
        // needs a DbContext that the failing one is not. Holding a scoped DbContext here would be the very defect they
        // exist to avoid — and, with Ordering's ValidateScopes turned on, would not even resolve.
        services.AddSingleton<IFailedStripeWebhookStore, FailedStripeWebhookStore>();
        services.AddSingleton<IFailedStripeWebhookReplayer, FailedStripeWebhookReplayer>();

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

        // Payment audit Stage 10 (D13): processed Stripe webhook events are kept for 30 days, where nothing deleted them.
        services.AddHostedService<ProcessedStripeWebhookEventCleanupService>();

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

    /// <summary>
    /// The Stripe client for <paramref name="settings"/>. With no key configured, the client has none. That is the case
    /// with Stripe off: the tracked appsettings.json ships an empty key.
    /// <para>Stripe.net throws on an empty or whitespace key, and <c>PaymentRefunder</c> resolves the Stripe service even to
    /// refund a simulated payment. Such a client fails only if something actually calls Stripe.</para>
    /// </summary>
    public static Stripe.IStripeClient CreateStripeClient(StripeSettings settings)
        => new Stripe.StripeClient(apiKey: string.IsNullOrWhiteSpace(settings.SecretKey) ? null : settings.SecretKey);

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
