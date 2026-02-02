using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
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

        if (!user.IsActive || user.IsDeleted)
        {
            _logger.LogWarning("Password reset attempt for disabled user. UserId={UserId}, IsActive={IsActive}, IsDeleted={IsDeleted}",
                user.Id, user.IsActive, user.IsDeleted);
            IdentityTelemetry.RecordPasswordReset(false);
            return Result<ResetPasswordResponse>.Failure(new Error("Auth.AccountDisabled", "Account is disabled"));
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Password reset failed. UserId={UserId}, Errors={Errors}", user.Id, errors);
            IdentityTelemetry.RecordPasswordReset(false);
            return Result<ResetPasswordResponse>.Failure(new Error("Auth.ResetFailed", errors));
        }

        // Revoke all refresh tokens after password reset (security measure)
        await _refreshTokenRepository.RevokeAllUserTokensAsync(
            user.Id, 
            "Password reset", 
            cancellationToken: cancellationToken);

        _logger.LogInformation("Password reset successfully. UserId={UserId}, Email={Email}", user.Id, user.Email);
        IdentityTelemetry.RecordPasswordReset(true);

        return Result<ResetPasswordResponse>.Success(new ResetPasswordResponse
        {
            Success = true,
            Message = "Password has been reset successfully"
        });
    }
}
