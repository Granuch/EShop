using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.Login;

/// <summary>
/// Handler for user login
/// </summary>
public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        ILogger<LoginCommandHandler> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // Find user by email
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            _logger.LogWarning("Login attempt with non-existent email: {Email}", request.Email);
            return Result<LoginResponse>.Failure(new Error("Auth.InvalidCredentials", "Invalid email or password"));
        }

        // Check if user is active and not deleted
        if (!user.IsActive || user.IsDeleted)
        {
            _logger.LogWarning("Login attempt for inactive/deleted user: {Email}", request.Email);
            return Result<LoginResponse>.Failure(new Error("Auth.AccountDisabled", "Your account has been disabled"));
        }

        // Check account lockout
        if (await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("Login attempt for locked account: {Email}", request.Email);
            return Result<LoginResponse>.Failure(new Error("Auth.AccountLocked", "Your account is locked. Please try again later"));
        }

        // Verify password
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                _logger.LogWarning("Account locked after failed attempts: {Email}", request.Email);
                return Result<LoginResponse>.Failure(new Error("Auth.AccountLocked", "Your account is locked due to multiple failed attempts"));
            }
            
            _logger.LogWarning("Invalid password for user: {Email}", request.Email);
            return Result<LoginResponse>.Failure(new Error("Auth.InvalidCredentials", "Invalid email or password"));
        }

        // Check if 2FA is enabled
        if (user.TwoFactorEnabled && string.IsNullOrEmpty(request.TwoFactorCode))
        {
            return Result<LoginResponse>.Success(new LoginResponse
            {
                Requires2FA = true
            });
        }

        // Verify 2FA code if provided (simplified implementation)
        if (user.TwoFactorEnabled && !string.IsNullOrEmpty(request.TwoFactorCode))
        {
            var is2faValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, 
                _userManager.Options.Tokens.AuthenticatorTokenProvider, 
                request.TwoFactorCode);
            
            if (!is2faValid)
            {
                return Result<LoginResponse>.Failure(new Error("Auth.Invalid2FA", "Invalid two-factor code"));
            }
        }

        // Generate tokens
        var accessToken = await _tokenService.GenerateAccessTokenAsync(user, cancellationToken);
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user.Id, request.IpAddress ?? "unknown", cancellationToken);

        // Update last login info
        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = request.IpAddress;
        await _userManager.UpdateAsync(user);

        // Get user roles
        var roles = await _userManager.GetRolesAsync(user);

        _logger.LogInformation("User logged in successfully: {UserId}", user.Id);

        return Result<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 3600, // 1 hour
            TokenType = "Bearer",
            Requires2FA = false,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Roles = roles.ToList()
            }
        });
    }
}
