using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EShop.BuildingBlocks.Domain;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Domain.Security;
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
    private readonly ICachedUserRolesService _cachedUserRolesService;
    private readonly IRevokedTokenCache _revokedTokenCache;
    private readonly ILogger<TokenService>? _logger;

    /// <summary>
    /// <c>ICachedUserRolesService</c> and <c>IRevokedTokenCache</c> are required, not optional.
    /// They used to default to null, and both are registered — so the defaults never applied in
    /// practice and only served to make a *dropped* registration silent: the revoked-token check
    /// would have been skipped entirely (`_revokedTokenCache != null` guards it), turning a
    /// wiring mistake into a security hole that no test would catch. Fail at resolution instead.
    /// The logger stays optional so the unit tests can construct the service directly.
    /// </summary>
    public TokenService(
        IOptions<JwtSettings> jwtSettings,
        UserManager<ApplicationUser> userManager,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        ICachedUserRolesService cachedUserRolesService,
        IRevokedTokenCache revokedTokenCache,
        ILogger<TokenService>? logger = null)
    {
        _jwtSettings = jwtSettings.Value;
        _userManager = userManager;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _cachedUserRolesService = cachedUserRolesService ?? throw new ArgumentNullException(nameof(cachedUserRolesService));
        _revokedTokenCache = revokedTokenCache ?? throw new ArgumentNullException(nameof(revokedTokenCache));
        _logger = logger;
    }

    /// <inheritdoc />
    public int AccessTokenExpirationSeconds => _jwtSettings.AccessTokenExpirationMinutes * 60;

    public async Task<string> GenerateAccessTokenAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        // The service is required now, so there is no UserManager fallback branch to take.
        // CachedUserRolesService already falls back to the database internally when the cache
        // is unreachable, so the old branch was a second, redundant fallback.
        var roles = await _cachedUserRolesService.GetRolesAsync(user, cancellationToken);

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

    /// <summary>
    /// Mints a refresh token, returning the plaintext to hand to the client and the hash to
    /// persist. This was duplicated verbatim in <see cref="RotateRefreshTokenAsync"/>, which is
    /// exactly the shape that lets one of the two sites keep storing plaintext after the other
    /// is fixed.
    /// </summary>
    private static (string Plaintext, string Hash) CreateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        var plaintext = Convert.ToBase64String(randomBytes);
        return (plaintext, RefreshTokenHasher.Hash(plaintext));
    }

    private async Task<string> GenerateRefreshTokenInternalAsync(string userId, string ipAddress, bool saveChanges, CancellationToken cancellationToken = default)
    {
        var (tokenString, tokenHash) = CreateRefreshToken();

        var refreshToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = tokenHash,
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
            await _revokedTokenCache.AddRevokedTokenAsync(token, refreshToken.ExpiresAt, cancellationToken);

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
        // Check revoked token cache first for faster validation. This is the check that a
        // dropped registration used to skip silently — see the constructor.
        var isRevoked = await _revokedTokenCache.IsTokenRevokedAsync(token, cancellationToken);
        if (isRevoked == true)
        {
            _logger?.LogDebug("Token found in revoked cache, rejecting without DB lookup");
            return (false, null, null);
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

        var (newTokenString, newTokenHash) = CreateRefreshToken();

        // Only manage our own transaction if one isn't already active
        // (e.g., TransactionBehavior may have started one at the pipeline level)
        var ownsTransaction = !_unitOfWork.HasActiveTransaction;

        if (ownsTransaction)
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            // Step 1: Atomically revoke the old token (with race condition protection)
            var affectedRows = await _refreshTokenRepository.RevokeTokenByHashAtomicallyAsync(
                oldToken.TokenHash,
                now,
                ipAddress,
                newTokenHash,
                "Rotated",
                cancellationToken);

            if (affectedRows == 0)
            {
                if (ownsTransaction)
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw new InvalidOperationException("Token has already been used or revoked");
            }

            // Step 2: Create and persist the new refresh token
            var newRefreshToken = new RefreshTokenEntity
            {
                Id = Guid.NewGuid(),
                TokenHash = newTokenHash,
                UserId = oldToken.UserId,
                CreatedAt = now,
                ExpiresAt = now.AddDays(_jwtSettings.RefreshTokenExpirationDays),
                CreatedByIp = ipAddress
            };

            await _refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);

            // Step 3: Commit only if we own the transaction.
            // If an outer transaction exists (e.g. MediatR TransactionBehavior),
            // defer persistence to that owner to avoid nested save semantics.
            if (ownsTransaction)
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return newTokenString;
        }
        catch
        {
            if (ownsTransaction)
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
