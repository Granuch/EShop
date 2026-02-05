using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Application.Telemetry;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.RefreshToken;

/// <summary>
/// Handler for refreshing access token
/// </summary>
public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>
{
    private readonly ITokenService _tokenService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        ITokenService tokenService,
        UserManager<ApplicationUser> userManager,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _tokenService = tokenService;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<RefreshTokenResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        using var timer = IdentityTelemetry.MeasureTokenGeneration();

        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            IdentityTelemetry.RecordTokenRefreshFailure("missing_token");
            return Result<RefreshTokenResponse>.Failure(new Error("Auth.InvalidToken", "Refresh token is required"));
        }

        // Validate refresh token from database
        var (isValid, user, oldToken) = await _tokenService.ValidateRefreshTokenAsync(request.RefreshToken, cancellationToken);

        if (!isValid || user == null || oldToken == null)
        {
            _logger.LogWarning("Invalid refresh token attempt. IP={IpAddress}", request.IpAddress);
            IdentityTelemetry.RecordTokenRefreshFailure("invalid_token");
            return Result<RefreshTokenResponse>.Failure(new Error("Auth.InvalidToken", "Invalid or expired refresh token"));
        }

        // Check if user is still active
        if (!user.IsActive || user.IsDeleted)
        {
            _logger.LogWarning("Refresh token attempt for disabled user. UserId={UserId}, IsActive={IsActive}, IsDeleted={IsDeleted}",
                user.Id, user.IsActive, user.IsDeleted);
            IdentityTelemetry.RecordTokenRefreshFailure("account_disabled");
            return Result<RefreshTokenResponse>.Failure(new Error("Auth.AccountDisabled", "Account is disabled"));
        }

        // Rotate refresh token (revoke old, create new)
        // This is an atomic operation that prevents concurrent token reuse
        string newRefreshToken;
        try
        {
            newRefreshToken = await _tokenService.RotateRefreshTokenAsync(
                oldToken, 
                request.IpAddress ?? "unknown", 
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Token rotation failed (possible replay attack). UserId={UserId}, IP={IpAddress}, Error={Error}",
                user.Id, request.IpAddress, ex.Message);
            IdentityTelemetry.RecordTokenRefreshFailure("rotation_failed");
            return Result<RefreshTokenResponse>.Failure(new Error("Auth.TokenAlreadyUsed", "This refresh token has already been used"));
        }

        // Generate new access token
        var accessToken = await _tokenService.GenerateAccessTokenAsync(user, cancellationToken);

        _logger.LogInformation("Token refreshed successfully. UserId={UserId}, IP={IpAddress}",
            user.Id, request.IpAddress);
        IdentityTelemetry.RecordTokenRefreshSuccess();

        return Result<RefreshTokenResponse>.Success(new RefreshTokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = _tokenService.AccessTokenExpirationSeconds
        });
    }
}
