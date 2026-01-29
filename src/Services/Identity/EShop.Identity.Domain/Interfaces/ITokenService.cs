using EShop.Identity.Domain.Entities;

namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Service for JWT token generation and validation
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates an access token with user claims
    /// </summary>
    Task<string> GenerateAccessTokenAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a refresh token for the user and stores it
    /// </summary>
    Task<string> GenerateRefreshTokenAsync(string userId, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the given access token
    /// </summary>
    Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the given refresh token
    /// </summary>
    Task RevokeTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates refresh token and returns associated user
    /// </summary>
    Task<(bool IsValid, ApplicationUser? User, RefreshTokenEntity? Token)> ValidateRefreshTokenAsync(
        string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates refresh token - revokes old one and creates new
    /// </summary>
    Task<string> RotateRefreshTokenAsync(
        RefreshTokenEntity oldToken, 
        string ipAddress, 
        CancellationToken cancellationToken = default);
}
