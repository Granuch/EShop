using System.Collections.Concurrent;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Extensions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.Notification.IntegrationTests.Fixtures;

/// <summary>
/// Admin panel S13. The Testing host with a bus that actually delivers: MassTransit's in-memory test harness, the
/// <b>real</b> seven consumers registered through <c>AddNotificationConsumers</c>, and the production endpoint name
/// formatter — so a resend is received by the same consumer, from the same queue name, as in production. Under Testing
/// <c>RabbitMQ:Host</c> is blank and <c>AddEShopBus</c> registers nothing; without this a resend has nowhere to go.
///
/// <para>
/// The two external calls are replaced: <see cref="RecordingEmailService"/> records every send instead of opening an SMTP
/// connection, and the contact lookup always finds <see cref="CustomerEmail"/>.
/// </para>
/// </summary>
public sealed class DeliveringNotificationApiFactory : NotificationApiFactory
{
    public const string CustomerEmail = "customer@eshop.test";

    /// <summary>A test send to this address fails as an unreachable SMTP server would.</summary>
    public const string UnreachableSmtpAddress = "smtp-down@eshop.test";

    public static readonly TimeSpan HarnessTimeout = TimeSpan.FromSeconds(10);

    public RecordingEmailService Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Email);
            services.RemoveAll<IUserContactResolver>();
            services.AddSingleton<IUserContactResolver>(new FoundResolver());

            services.AddMassTransitTestHarness(bus =>
            {
                bus.SetTestTimeouts(testTimeout: HarnessTimeout, testInactivityTimeout: HarnessTimeout);
                bus.SetEndpointNameFormatter(MassTransitServiceCollectionExtensions.CreateEndpointNameFormatter(
                    ServiceCollectionExtensions.MessagingServiceName));
                bus.AddNotificationConsumers();
            });
        });
    }

    private sealed class FoundResolver : IUserContactResolver
    {
        public Task<RecipientLookup> ResolveAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(RecipientLookup.Found(new RecipientAddress(CustomerEmail, "Customer")));
    }

    /// <summary>Every send, by the method it came through; thread-safe, because the consumers run on the bus's threads.</summary>
    public sealed class RecordingEmailService : IEmailService
    {
        private readonly ConcurrentQueue<(string Method, string Recipient, Guid? OrderId)> _sends = new();

        public IReadOnlyList<(string Method, string Recipient, Guid? OrderId)> Sends => _sends.ToArray();

        public Task<string> SendOrderConfirmationAsync(RecipientAddress recipient, OrderConfirmationEmailModel model, CancellationToken ct = default)
            => Record(nameof(SendOrderConfirmationAsync), recipient, model.OrderId);

        public Task<string> SendOrderShippedAsync(RecipientAddress recipient, OrderShippedEmailModel model, CancellationToken ct = default)
            => Record(nameof(SendOrderShippedAsync), recipient, model.OrderId);

        public Task<string> SendPaymentCreatedAsync(RecipientAddress recipient, PaymentCreatedEmailModel model, CancellationToken ct = default)
            => Record(nameof(SendPaymentCreatedAsync), recipient, model.OrderId);

        public Task<string> SendPaymentCompletedAsync(RecipientAddress recipient, PaymentCompletedEmailModel model, CancellationToken ct = default)
            => Record(nameof(SendPaymentCompletedAsync), recipient, model.OrderId);

        public Task<string> SendPaymentFailedAsync(RecipientAddress recipient, PaymentFailedEmailModel model, CancellationToken ct = default)
            => Record(nameof(SendPaymentFailedAsync), recipient, model.OrderId);

        public Task<string> SendPaymentRefundedAsync(RecipientAddress recipient, PaymentRefundedEmailModel model, CancellationToken ct = default)
            => Record(nameof(SendPaymentRefundedAsync), recipient, model.OrderId);

        public Task<string> SendPasswordResetAsync(RecipientAddress recipient, PasswordResetEmailModel model, CancellationToken ct = default)
            => Record(nameof(SendPasswordResetAsync), recipient, null);

        private Task<string> Record(string method, RecipientAddress recipient, Guid? orderId)
        {
            if (recipient.Email == UnreachableSmtpAddress)
            {
                throw new System.Net.Sockets.SocketException(10061);
            }

            _sends.Enqueue((method, recipient.Email, orderId));
            return Task.FromResult($"<{Guid.NewGuid():N}@test.eshop>");
        }
    }
}
