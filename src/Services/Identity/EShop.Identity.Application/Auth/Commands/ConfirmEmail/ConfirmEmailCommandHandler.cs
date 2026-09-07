using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Application.Telemetry;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.ConfirmEmail;

/// <summary>
/// Handler for email confirmation.
/// The email confirmation and the integration event share one transaction, opened and committed
/// by TransactionBehavior because ConfirmEmailCommand is ITransactionalCommand. This handler
/// deliberately takes no IUnitOfWork — see the note in Handle.
/// </summary>
public class ConfirmEmailCommandHandler : IRequestHandler<ConfirmEmailCommand, Result<ConfirmEmailResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ConfirmEmailCommandHandler> _logger;

    public ConfirmEmailCommandHandler(
        UserManager<ApplicationUser> userManager,
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<ConfirmEmailCommandHandler> logger)
    {
        _userManager = userManager;
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<ConfirmEmailResponse>> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        using var activity = IdentityActivitySource.Source.StartActivity("Identity.ConfirmEmail");
        activity?.SetTag("user.id", request.UserId);

        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null)
        {
            _logger.LogWarning("Email confirmation attempt for non-existent user. UserId={UserId}", request.UserId);
            IdentityTelemetry.RecordEmailConfirmation(false);
            activity?.SetStatus(ActivityStatusCode.Error, "user_not_found");
            return Result<ConfirmEmailResponse>.Failure(new Error("Auth.UserNotFound", "User not found"));
        }

        if (user.EmailConfirmed)
        {
            _logger.LogInformation("Email already confirmed. UserId={UserId}", user.Id);
            return Result<ConfirmEmailResponse>.Success(new ConfirmEmailResponse
            {
                Success = true,
                Message = "Email already confirmed"
            });
        }

        // ConfirmEmailCommand is ITransactionalCommand, so TransactionBehavior has already opened
        // the transaction that covers both the confirmation and the outbox enqueue below. This
        // handler must NOT drive the unit of work itself: an inner commit would close the
        // transaction the behavior still believes it owns, leaving the behavior's own commit and
        // rollback operating on nothing.
        //
        // On the failure path we return a Result rather than throwing, and TransactionBehavior
        // commits on any non-exception return. That is safe here because UserManager
        // .ConfirmEmailAsync writes nothing when token validation fails — it does not touch the
        // user or the access-failed count — so the committed transaction is empty. The outbox
        // event is enqueued only after the success check, so a failure can never publish it.
        var result = await _userManager.ConfirmEmailAsync(user, request.Token);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Email confirmation failed. UserId={UserId}, Errors={Errors}", user.Id, errors);
            IdentityTelemetry.RecordEmailConfirmation(false);
            activity?.SetStatus(ActivityStatusCode.Error, "invalid_token");
            return Result<ConfirmEmailResponse>.Failure(new Error("Auth.InvalidToken", "Invalid or expired confirmation token"));
        }

        _outbox.Enqueue(new UserEmailConfirmedIntegrationEvent
        {
            UserId = user.Id,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        _logger.LogInformation("Email confirmed successfully. UserId={UserId}", user.Id);
        IdentityTelemetry.RecordEmailConfirmation(true);

        return Result<ConfirmEmailResponse>.Success(new ConfirmEmailResponse
        {
            Success = true,
            Message = "Email confirmed successfully"
        });
    }
}
