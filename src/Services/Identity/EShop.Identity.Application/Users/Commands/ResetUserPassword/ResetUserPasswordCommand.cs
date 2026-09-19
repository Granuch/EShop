using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.ResetUserPassword;

/// <summary>
/// Sends a password-reset link on a user's behalf (Admin panel S7, endpoint #12, decision Q2a).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no <c>newPassword</c>, and no <c>mode</c>.</b> The plan sketched
/// <c>mode: email|set</c>; Q2a settled it as email-only, and the shape of the endpoint is the
/// decision: an admin cannot choose a password, so there is never a password an admin knows and
/// the user does not. That also removed the need for a <c>MustChangePasswordAt</c> state and a
/// login-path change to honour it.
/// </para>
/// <para>
/// Unlike <c>ForgotPasswordCommandHandler</c> this answers honestly for a missing user. That
/// handler's identical-response-and-identical-work dance exists to stop an anonymous caller
/// enumerating accounts; here the caller is already an authenticated administrator who can list
/// every user, so pretending would hide a typo rather than protect anything.
/// </para>
/// <para>
/// The event is <c>ISensitivePayloadEvent</c>, so the live token in <c>outbox_messages</c> is
/// redacted as soon as the row goes terminal instead of sitting readable for the seven-day
/// retention window (SEC-05).
/// </para>
/// </remarks>
public record ResetUserPasswordCommand : IRequest<Result<Unit>>, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
}

public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ResetUserPasswordCommandHandler> _logger;

    public ResetUserPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<ResetUserPasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        // No SaveChangesAsync: the command is ITransactionalCommand, so the behavior's commit is
        // what persists the outbox row. Adding one here is BUG-07's shape.
        _outbox.Enqueue(new PasswordResetRequestedIntegrationEvent
        {
            UserId = user.Id,
            ResetToken = token,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        _logger.LogInformation("Admin triggered a password reset email. UserId={UserId}", request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
