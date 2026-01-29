using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Account.Commands.Verify2FA;

/// <summary>
/// Handler for verifying and enabling two-factor authentication
/// </summary>
public class Verify2FACommandHandler : IRequestHandler<Verify2FACommand, Result<Verify2FAResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<Verify2FACommandHandler> _logger;

    public Verify2FACommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<Verify2FACommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<Verify2FAResponse>> Handle(Verify2FACommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null)
        {
            return Result<Verify2FAResponse>.Failure(new Error("Account.UserNotFound", "User not found"));
        }

        // Verify the code
        var isCodeValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, 
            _userManager.Options.Tokens.AuthenticatorTokenProvider, 
            request.Code);

        if (!isCodeValid)
        {
            _logger.LogWarning("Invalid 2FA verification code for user: {UserId}", user.Id);
            return Result<Verify2FAResponse>.Failure(new Error("Account.InvalidCode", "Invalid verification code"));
        }

        // Enable 2FA
        await _userManager.SetTwoFactorEnabledAsync(user, true);

        // Generate recovery codes
        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        _logger.LogInformation("2FA enabled for user: {UserId}", user.Id);

        return Result<Verify2FAResponse>.Success(new Verify2FAResponse
        {
            Success = true,
            RecoveryCodes = recoveryCodes?.ToArray() ?? [],
            Message = "Two-factor authentication has been enabled. Save your recovery codes in a safe place."
        });
    }
}
