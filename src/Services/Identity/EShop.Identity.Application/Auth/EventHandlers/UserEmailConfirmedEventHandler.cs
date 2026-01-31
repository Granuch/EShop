using EShop.Identity.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.EventHandlers;

/// <summary>
/// Handler for UserEmailConfirmedEvent
/// Sends confirmation email, updates audit log
/// </summary>
public class UserEmailConfirmedEventHandler : INotificationHandler<UserEmailConfirmedEvent>
{
    private readonly ILogger<UserEmailConfirmedEventHandler> _logger;

    public UserEmailConfirmedEventHandler(ILogger<UserEmailConfirmedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(UserEmailConfirmedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "User email confirmed: UserId={UserId}, Email={Email}, OccurredOn={OccurredOn}",
            notification.UserId,
            notification.Email,
            notification.OccurredOn);

        // TODO: Send email confirmed notification
        // TODO: Publish integration event

        return Task.CompletedTask;
    }
}
