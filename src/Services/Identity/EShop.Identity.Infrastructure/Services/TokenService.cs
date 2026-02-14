using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EShop.BuildingBlocks.Domain;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Infrastructure.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Identity.Infrastructure.Services;

/// <summary>
/// Service for JWT token generation and validation.
/// Uses Redis caching for:
/// - User roles (via ICachedUserRolesService)
/// - Revoked token tracking (via IRevokedTokenCache)
/// </summary>
public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICachedUserRolesService? _cachedUserRolesService;
    private readonly IRevokedTokenCache? _revokedTokenCache;
    private readonly ILogger<TokenService>? _logger;

    public TokenService(
        IOptions<JwtSettings> jwtSettings, 
        UserManager<ApplicationUser> userManager,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        ICachedUserRolesService? cachedUserRolesService = null,
        IRevokedTokenCache? revokedTokenCache = null,
        ILogger<TokenService>? logger = null)
    {
        _jwtSettings = jwtSettings.Value;
        _userManager = userManager;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _cachedUserRolesService = cachedUserRolesService;
        _revokedTokenCache = revokedTokenCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public int AccessTokenExpirationSeconds => _jwtSettings.AccessTokenExpirationMinutes * 60;

    public async Task<string> GenerateAccessTokenAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        // Use cached roles service if available, otherwise fallback to UserManager
        IList<string> roles;
        if (_cachedUserRolesService != null)
        {
            roles = await _cachedUserRolesService.GetRolesAsync(user, cancellationToken);
        }
        else
        {
            roles = await _userManager.GetRolesAsync(user);
        }

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
            await _unitOfWork.SaveChangesAsync(cancellationToken);
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
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Add to revoked token cache for faster validation
            if (_revokedTokenCache != null)
            {
                await _revokedTokenCache.AddRevokedTokenAsync(token, refreshToken.ExpiresAt, cancellationToken);
            }

            _logger?.LogInformation("Refresh token revoked and added to cache. UserId={UserId}", refreshToken.UserId);
        }
    }

    /// <summary>
    /// Validates refresh token and returns associated user.
    /// Uses revoked token cache for faster validation.
    /// </summary>
    public async Task<(bool IsValid, ApplicationUser? User, RefreshTokenEntity? Token)> ValidateRefreshTokenAsync(
        string token, CancellationToken cancellationToken = default)
    {
        // Check revoked token cache first for faster validation
        if (_revokedTokenCache != null)
        {
            var isRevoked = await _revokedTokenCache.IsTokenRevokedAsync(token, cancellationToken);
            if (isRevoked == true)
            {
                _logger?.LogDebug("Token found in revoked cache, rejecting without DB lookup");
                return (false, null, null);
            }
        }

        var refreshToken = await _refreshTokenRepository.GetByTokenAsync(token, cancellationToken);
        
        if (refreshToken == null)
            return (false, null, null);

        if (!refreshToken.IsActive)
            return (false, null, refreshToken);

        return (true, refreshToken.User, refreshToken);
    }

    /// <summary>
    /// Rotates refresh token - revokes old one and creates new
    /// CRITICAL: Uses explicit database transaction to ensure atomicity.
    /// This prevents orphaned tokens if SaveChangesAsync fails after revocation.
    /// Security invariant: Token rotation must be all-or-nothing.
    /// </summary>
    public async Task<string> RotateRefreshTokenAsync(
        RefreshTokenEntity oldToken, 
        string ipAddress, 
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Generate new token string
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var newTokenString = Convert.ToBase64String(randomBytes);

        // Begin explicit transaction to ensure atomicity
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            // Step 1: Atomically revoke the old token (with race condition protection)
            var affectedRows = await _refreshTokenRepository.RevokeTokenAtomicallyAsync(
                oldToken.Token,
                now,
                ipAddress,
                newTokenString,
                "Rotated",
                cancellationToken);

            if (affectedRows == 0)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw new InvalidOperationException("Token has already been used or revoked");
            }

            // Step 2: Create and persist the new refresh token
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

            // Step 3: Commit transaction - both operations succeed or both fail
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return newTokenString;
        }
        catch
        {
            // Ensure rollback on any failure
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
