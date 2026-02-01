using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Application.Telemetry;
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
        using var timer = IdentityTelemetry.MeasureLoginDuration();

        var user = await _userManager.FindByEmailAsync(request.Email);

        bool isPasswordValid = false;
        bool isLockedOut = false;
        string? failureReason = null;

        if (user != null)
        {
            var signInResult = await _signInManager.CheckPasswordSignInAsync(
                user, 
                request.Password, 
                lockoutOnFailure: true);

            isPasswordValid = signInResult.Succeeded;
            isLockedOut = signInResult.IsLockedOut;
        }
        else
        {
            var passwordHasher = new PasswordHasher<ApplicationUser>();
            var dummyUser = new ApplicationUser { Email = request.Email };

            const string dummyHash = "AQAAAAIAAYagAAAAEJKt5pCLQs+Y8VK0GBH5RLhHMDWNz0W9EJkWJu7gf9LVvHdFYjQYGKLVQFQVnRPW3Q==";
            passwordHasher.VerifyHashedPassword(dummyUser, dummyHash, request.Password);

            isPasswordValid = false;
            failureReason = "user_not_found";
        }

        bool canProceed = true;

        if (user == null)
        {
            canProceed = false;
            failureReason = "user_not_found";
        }
        else if (!user.IsActive || user.IsDeleted)
        {
            canProceed = false;
            failureReason = "account_disabled";
            _logger.LogWarning("Login attempt for inactive/deleted user. UserId={UserId}, Email={Email}, IsActive={IsActive}, IsDeleted={IsDeleted}",
                user.Id, request.Email, user.IsActive, user.IsDeleted);
        }
        else if (isLockedOut || await _userManager.IsLockedOutAsync(user))
        {
            canProceed = false;
            failureReason = "account_locked";
            _logger.LogWarning("Login attempt for locked account. UserId={UserId}, Email={Email}, LockoutEnd={LockoutEnd}",
                user.Id, request.Email, user.LockoutEnd);
        }
        else if (!isPasswordValid)
        {
            canProceed = false;
            failureReason = "invalid_password";
            _logger.LogWarning("Invalid password attempt. UserId={UserId}, Email={Email}, IP={IpAddress}",
                user.Id, request.Email, request.IpAddress);
        }

        if (!canProceed)
        {
            IdentityTelemetry.RecordLoginFailure(failureReason ?? "unknown");

            var errorMessage = failureReason == "account_locked"
                ? "Your account is locked. Please try again later"
                : "Invalid email or password";

            var errorCode = failureReason == "account_locked" 
                ? "Auth.AccountLocked" 
                : "Auth.InvalidCredentials";

            return Result<LoginResponse>.Failure(new Error(errorCode, errorMessage));
        }

        if (user!.TwoFactorEnabled && string.IsNullOrEmpty(request.TwoFactorCode))
        {
            _logger.LogInformation("2FA required for login. UserId={UserId}", user.Id);
            IdentityTelemetry.RecordLogin2FARequired();
            return Result<LoginResponse>.Success(new LoginResponse
            {
                Requires2FA = true
            });
        }

        if (user.TwoFactorEnabled && !string.IsNullOrEmpty(request.TwoFactorCode))
        {
            var is2faValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, 
                _userManager.Options.Tokens.AuthenticatorTokenProvider, 
                request.TwoFactorCode);

            if (!is2faValid)
            {
                _logger.LogWarning("Invalid 2FA code. UserId={UserId}", user.Id);
                IdentityTelemetry.RecordLoginFailure("invalid_2fa");
                return Result<LoginResponse>.Failure(new Error("Auth.Invalid2FA", "Invalid two-factor code"));
            }
        }

        var accessToken = await _tokenService.GenerateAccessTokenAsync(user, cancellationToken);
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user.Id, request.IpAddress ?? "unknown", cancellationToken);

        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = request.IpAddress;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);

        _logger.LogInformation("User logged in successfully. UserId={UserId}, Email={Email}, IP={IpAddress}, Roles={Roles}",
            user.Id, user.Email, request.IpAddress, string.Join(",", roles));
        IdentityTelemetry.RecordLoginSuccess();

        return Result<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _tokenService.AccessTokenExpirationSeconds,
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
