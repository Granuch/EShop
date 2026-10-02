using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Domain.Security;
using EShop.Identity.Application.Telemetry;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.ResetPassword;

/// <summary>
/// Handler for password reset
/// </summary>
public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Result<ResetPasswordResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ResetPasswordCommandHandler> _logger;

    public ResetPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IRefreshTokenRepository refreshTokenRepository,
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<ResetPasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _refreshTokenRepository = refreshTokenRepository;
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<ResetPasswordResponse>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null)
        {
            _logger.LogWarning("Password reset attempt for non-existent user. UserId={UserId}", request.UserId);
            IdentityTelemetry.RecordPasswordReset(false);
            return Result<ResetPasswordResponse>.Failure(new Error("Auth.UserNotFound", "Invalid password reset request"));
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Password reset attempt for disabled user. UserId={UserId}, IsActive={IsActive}, IsDeleted={IsDeleted}",
                user.Id, user.IsActive, user.IsDeleted);
            IdentityTelemetry.RecordPasswordReset(false);
            return Result<ResetPasswordResponse>.Failure(new Error("Auth.AccountDisabled", "Account is disabled"));
        }

        // Reset password (UserManager handles its own transaction)
        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Password reset failed. UserId={UserId}, Errors={Errors}", user.Id, errors);
            IdentityTelemetry.RecordPasswordReset(false);
            return Result<ResetPasswordResponse>.Failure(new Error("Auth.ResetFailed", errors));
        }

        // Revoking every refresh token is part of the reset, not a follow-up to it: a reset
        // that reports success while the old sessions stay valid is an auth bypass, and here
        // the reset is typically driven by a user who believes their account is compromised.
        // The command is ITransactionalCommand, so this write joins the behavior's ambient
        // transaction and TransactionBehavior's CommitTransactionAsync is what persists it —
        // there is deliberately no SaveChangesAsync and no try/catch here. If the revoke
        // throws, the behavior rolls the reset back and the caller sees the failure.
        await _refreshTokenRepository.RevokeAllUserTokensAsync(
            user.Id,
            "Password reset",
            cancellationToken: cancellationToken);

        // A reset token only ever travels by email, so redeeming one proves the caller reads that
        // mailbox — exactly what confirmation proves. Without this, a user an administrator invited
        // (an unconfirmed account whose only email is the reset link) could set a password and
        // still never place an order — or, in strict mode (SignIn.RequireConfirmedEmail), sign in.
        //
        // Set only AFTER ResetPasswordAsync succeeded: TransactionBehavior commits on a failure
        // Result too, and a tracked user flagged before the token check would be confirmed by a
        // request whose token was wrong. The tracked change is persisted by that same commit.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            _outbox.Enqueue(new UserEmailConfirmedIntegrationEvent
            {
                UserId = user.Id,
                CorrelationId = _currentUserContext.CorrelationId
            }, _currentUserContext.CorrelationId);
            _logger.LogInformation("Email confirmed by a password reset. UserId={UserId}", user.Id);
        }

        _logger.LogInformation("Password reset successfully. UserId={UserId}", user.Id);
        IdentityTelemetry.RecordPasswordReset(true);

        return Result<ResetPasswordResponse>.Success(new ResetPasswordResponse
        {
            Success = true,
            Message = "Password has been reset successfully"
        });
    }
}
