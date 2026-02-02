using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Application.Telemetry;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Account.Commands.ChangePassword;

/// <summary>
/// Handler for changing user password
/// </summary>
public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result<ChangePasswordResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IRefreshTokenRepository refreshTokenRepository,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _refreshTokenRepository = refreshTokenRepository;
        _logger = logger;
    }

    public async Task<Result<ChangePasswordResponse>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null)
        {
            IdentityTelemetry.RecordPasswordChange(false);
            return Result<ChangePasswordResponse>.Failure(new Error("Account.NotFound", "User not found"));
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to change password. UserId={UserId}, Errors={Errors}", request.UserId, errors);
            IdentityTelemetry.RecordPasswordChange(false);
            return Result<ChangePasswordResponse>.Failure(new Error("Account.PasswordChangeFailed", errors));
        }

        // Revoke all refresh tokens after password change (security measure)
        await _refreshTokenRepository.RevokeAllUserTokensAsync(
            user.Id,
            "Password changed",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Password changed successfully. UserId={UserId}, Email={Email}", request.UserId, user.Email);
        IdentityTelemetry.RecordPasswordChange(true);

        return Result<ChangePasswordResponse>.Success(new ChangePasswordResponse
        {
            Message = "Password changed successfully"
        });
    }
}
