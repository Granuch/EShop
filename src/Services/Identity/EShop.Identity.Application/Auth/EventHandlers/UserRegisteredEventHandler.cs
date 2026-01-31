using EShop.Identity.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.EventHandlers;

/// <summary>
/// Handler for UserRegisteredEvent
/// Sends welcome email, creates audit log, publishes integration event
/// </summary>
public class UserRegisteredEventHandler : INotificationHandler<UserRegisteredEvent>
{
    private readonly ILogger<UserRegisteredEventHandler> _logger;

    public UserRegisteredEventHandler(ILogger<UserRegisteredEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "User registered: UserId={UserId}, Email={Email}, FullName={FullName}, OccurredOn={OccurredOn}",
            notification.UserId,
            notification.Email,
            notification.FullName,
            notification.OccurredOn);

        // TODO: Send welcome email via EmailService
        // await _emailService.SendWelcomeEmailAsync(notification.Email, notification.FullName);

        // TODO: Publish integration event to message bus for other services
        // await _messageBus.PublishAsync(new UserRegisteredIntegrationEvent
        // {
        //     UserId = notification.UserId,
        //     Email = notification.Email,
        //     FullName = notification.FullName
        // });

        return Task.CompletedTask;
    }
}
