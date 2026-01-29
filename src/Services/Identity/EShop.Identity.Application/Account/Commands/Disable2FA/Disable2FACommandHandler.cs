using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Account.Commands.Disable2FA;

/// <summary>
/// Handler for disabling two-factor authentication
/// </summary>
public class Disable2FACommandHandler : IRequestHandler<Disable2FACommand, Result<Disable2FAResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<Disable2FACommandHandler> _logger;

    public Disable2FACommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<Disable2FACommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<Disable2FAResponse>> Handle(Disable2FACommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null)
        {
            return Result<Disable2FAResponse>.Failure(new Error("Account.UserNotFound", "User not found"));
        }

        if (!user.TwoFactorEnabled)
        {
            return Result<Disable2FAResponse>.Failure(new Error("Account.2FANotEnabled", "Two-factor authentication is not enabled"));
        }

        // Verify the code before disabling
        var isCodeValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, 
            _userManager.Options.Tokens.AuthenticatorTokenProvider, 
            request.Code);

        if (!isCodeValid)
        {
            _logger.LogWarning("Invalid 2FA code for disabling 2FA, user: {UserId}", user.Id);
            return Result<Disable2FAResponse>.Failure(new Error("Account.InvalidCode", "Invalid verification code"));
        }

        // Disable 2FA
        var result = await _userManager.SetTwoFactorEnabledAsync(user, false);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result<Disable2FAResponse>.Failure(new Error("Account.2FAError", errors));
        }

        // Reset the authenticator key
        await _userManager.ResetAuthenticatorKeyAsync(user);

        _logger.LogInformation("2FA disabled for user: {UserId}", user.Id);

        return Result<Disable2FAResponse>.Success(new Disable2FAResponse
        {
            Success = true,
            Message = "Two-factor authentication has been disabled"
        });
    }
}
