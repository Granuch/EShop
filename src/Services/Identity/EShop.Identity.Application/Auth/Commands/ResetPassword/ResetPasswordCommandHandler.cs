using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
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
    private readonly ILogger<ResetPasswordCommandHandler> _logger;

    public ResetPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IRefreshTokenRepository refreshTokenRepository,
        ILogger<ResetPasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _refreshTokenRepository = refreshTokenRepository;
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

        _logger.LogInformation("Password reset successfully. UserId={UserId}", user.Id);
        IdentityTelemetry.RecordPasswordReset(true);

        return Result<ResetPasswordResponse>.Success(new ResetPasswordResponse
        {
            Success = true,
            Message = "Password has been reset successfully"
        });
    }
}
