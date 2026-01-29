using System.Text;
using System.Text.Encodings.Web;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Account.Commands.Enable2FA;

/// <summary>
/// Handler for enabling two-factor authentication
/// </summary>
public class Enable2FACommandHandler : IRequestHandler<Enable2FACommand, Result<Enable2FAResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<Enable2FACommandHandler> _logger;
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    public Enable2FACommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<Enable2FACommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<Enable2FAResponse>> Handle(Enable2FACommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null)
        {
            return Result<Enable2FAResponse>.Failure(new Error("Account.UserNotFound", "User not found"));
        }

        if (user.TwoFactorEnabled)
        {
            return Result<Enable2FAResponse>.Failure(new Error("Account.2FAAlreadyEnabled", "Two-factor authentication is already enabled"));
        }

        // Get or generate the authenticator key
        var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
        
        if (string.IsNullOrEmpty(unformattedKey))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrEmpty(unformattedKey))
        {
            return Result<Enable2FAResponse>.Failure(new Error("Account.2FAError", "Failed to generate authenticator key"));
        }

        // Format the key for display
        var sharedKey = FormatKey(unformattedKey);
        
        // Generate QR code URI
        var email = await _userManager.GetEmailAsync(user);
        var qrCodeUri = GenerateQrCodeUri("EShop", email!, unformattedKey);

        _logger.LogInformation("2FA setup initiated for user: {UserId}", user.Id);

        return Result<Enable2FAResponse>.Success(new Enable2FAResponse
        {
            SharedKey = sharedKey,
            QrCodeUri = qrCodeUri,
            Message = "Scan the QR code with your authenticator app, then verify with a code"
        });
    }

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        var currentPosition = 0;
        
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        
        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }

        return result.ToString().ToLowerInvariant();
    }

    private static string GenerateQrCodeUri(string issuer, string email, string unformattedKey)
    {
        return string.Format(
            AuthenticatorUriFormat,
            UrlEncoder.Default.Encode(issuer),
            UrlEncoder.Default.Encode(email),
            unformattedKey);
    }
}
