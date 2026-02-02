using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Infrastructure.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Identity.Infrastructure.Services;

/// <summary>
/// Service for JWT token generation and validation
/// </summary>
public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRefreshTokenRepository _refreshTokenRepository;

    public TokenService(
        IOptions<JwtSettings> jwtSettings, 
        UserManager<ApplicationUser> userManager,
        IRefreshTokenRepository refreshTokenRepository)
    {
        _jwtSettings = jwtSettings.Value;
        _userManager = userManager;
        _refreshTokenRepository = refreshTokenRepository;
    }

    /// <inheritdoc />
    public int AccessTokenExpirationSeconds => _jwtSettings.AccessTokenExpirationMinutes * 60;

    public async Task<string> GenerateAccessTokenAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("firstName", user.FirstName),
            new("lastName", user.LastName)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<string> GenerateRefreshTokenAsync(string userId, string ipAddress, CancellationToken cancellationToken = default)
    {
        return await GenerateRefreshTokenInternalAsync(userId, ipAddress, saveChanges: true, cancellationToken);
    }

    private async Task<string> GenerateRefreshTokenInternalAsync(string userId, string ipAddress, bool saveChanges, CancellationToken cancellationToken = default)
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        var tokenString = Convert.ToBase64String(randomBytes);

        var refreshToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = tokenString,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedByIp = ipAddress
        };

        await _refreshTokenRepository.AddAsync(refreshToken, cancellationToken);

        if (saveChanges)
        {
            await _refreshTokenRepository.SaveChangesAsync(cancellationToken);
        }

        return tokenString;
    }

    public Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);

        try
        {
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task RevokeTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default)
    {
        var refreshToken = await _refreshTokenRepository.GetByTokenAsync(token, cancellationToken);
        
        if (refreshToken != null && refreshToken.IsActive)
        {
            refreshToken.RevokedAt = DateTime.UtcNow;
            refreshToken.RevokedByIp = ipAddress;
            refreshToken.RevokeReason = "Revoked by user";
            
            await _refreshTokenRepository.UpdateAsync(refreshToken, cancellationToken);
            await _refreshTokenRepository.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Validates refresh token and returns associated user
    /// </summary>
    public async Task<(bool IsValid, ApplicationUser? User, RefreshTokenEntity? Token)> ValidateRefreshTokenAsync(
        string token, CancellationToken cancellationToken = default)
    {
        var refreshToken = await _refreshTokenRepository.GetByTokenAsync(token, cancellationToken);
        
        if (refreshToken == null)
            return (false, null, null);

        if (!refreshToken.IsActive)
            return (false, null, refreshToken);

        return (true, refreshToken.User, refreshToken);
    }

    /// <summary>
    /// Rotates refresh token - revokes old one and creates new
    /// Uses atomic database operation to prevent race conditions in concurrent refresh scenarios
    /// </summary>
    public async Task<string> RotateRefreshTokenAsync(
        RefreshTokenEntity oldToken, 
        string ipAddress, 
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var newTokenString = Convert.ToBase64String(randomBytes);

        var affectedRows = await _refreshTokenRepository.RevokeTokenAtomicallyAsync(
            oldToken.Token,
            now,
            ipAddress,
            newTokenString,
            "Rotated",
            cancellationToken);

        if (affectedRows == 0)
        {
            throw new InvalidOperationException("Token has already been used or revoked");
        }

        var newRefreshToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = newTokenString,
            UserId = oldToken.UserId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedByIp = ipAddress
        };

        await _refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);
        await _refreshTokenRepository.SaveChangesAsync(cancellationToken);

        return newTokenString;
    }
}
