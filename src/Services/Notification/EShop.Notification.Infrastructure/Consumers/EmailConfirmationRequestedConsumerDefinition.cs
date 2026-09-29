using MassTransit;

namespace EShop.Notification.Infrastructure.Consumers;

/// <summary>
/// A confirmation message that exhausts its retries is discarded, not moved to
/// <c>notification_email_confirmation_requested_error</c> — the password-reset rule (Notification audit D6), for the
/// same reason: it carries a live token, and a parked copy is a readable secret with nothing to expire it. The
/// <c>NotificationLogs</c> row records the failure; the user asks for a new link through resend-confirmation.
/// </summary>
public sealed class EmailConfirmationRequestedConsumerDefinition : ConsumerDefinition<EmailConfirmationRequestedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<EmailConfirmationRequestedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.DiscardFaultedMessages();
    }
}
