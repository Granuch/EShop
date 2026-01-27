using EShop.Identity.Domain.Entities;

namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Service for JWT token generation and validation
/// </summary>
public interface ITokenService
{
    // TODO: Implement access token generation with claims
    Task<string> GenerateAccessTokenAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    // TODO: Implement refresh token generation
    Task<string> GenerateRefreshTokenAsync(string userId, string ipAddress, CancellationToken cancellationToken = default);

    // TODO: Implement token validation
    Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);

    // TODO: Implement token revocation
    Task RevokeTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default);

    // TODO: Implement refresh token storage in Redis or database
    // TODO: Add token rotation mechanism
}
