using MassTransit;

namespace EShop.Notification.Infrastructure.Consumers;

/// <summary>
/// Notification audit S3 (M5, D6). A password-reset message that exhausts its retries is discarded instead of being
/// moved to <c>notification_password_reset_requested_error</c>: it carries a live reset token (24 hours), and a parked
/// copy is a readable secret with nothing to expire it. The <c>NotificationLogs</c> row records the failure; the user
/// requests a new link. Every other notification endpoint keeps its error queue for replay.
/// <para>The endpoint name still comes from the bus's formatter (<c>notification_</c> prefix).</para>
/// </summary>
public sealed class PasswordResetRequestedConsumerDefinition : ConsumerDefinition<PasswordResetRequestedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PasswordResetRequestedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.DiscardFaultedMessages();
    }
}
