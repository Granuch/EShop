using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Domain.Security;
using EShop.Identity.Application.Telemetry;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.Login;

/// <summary>
/// Handler for user login with comprehensive brute-force protection
/// </summary>
public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly ILoginAttemptTracker _loginAttemptTracker;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        ILoginAttemptTracker loginAttemptTracker,
        ILogger<LoginCommandHandler> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _loginAttemptTracker = loginAttemptTracker;
        _logger = logger;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        using var timer = IdentityTelemetry.MeasureLoginDuration();

        // Step 1: Validate login attempt using brute-force protection
        // This checks all protection layers before processing credentials
        var ipAddress = request.IpAddress ?? "unknown";
        var validation = await _loginAttemptTracker.ValidateAttemptAsync(
            request.Email,
            ipAddress,
            cancellationToken);

        if (!validation.IsAllowed)
        {
            // Record metrics for blocked attempts
            switch (validation.BlockReason)
            {
                case BlockReason.Throttled:
                    IdentityTelemetry.RecordThrottledAttempt(validation.ThrottleDelaySeconds!.Value);
                    _logger.LogWarning(
                        "Login attempt throttled. HashedEmail={HashedEmail}, IP={IpAddress}, DelaySeconds={DelaySeconds}",
                        IdentifierHasher.HashShort(request.Email), ipAddress, validation.ThrottleDelaySeconds);
                    break;

                case BlockReason.AccountLocked:
                    IdentityTelemetry.RecordAccountLocked("brute_force_protection");
                    _logger.LogWarning(
                        "Login attempt for locked account. HashedEmail={HashedEmail}, IP={IpAddress}",
                        IdentifierHasher.HashShort(request.Email), ipAddress);
                    break;

                case BlockReason.IpBlocked:
                    IdentityTelemetry.RecordIpBlocked("excessive_failures");
                    _logger.LogWarning(
                        "Login attempt from blocked IP. IP={IpAddress}",
                        ipAddress);
                    break;

                case BlockReason.DistributedAttackDetected:
                    IdentityTelemetry.RecordDistributedAttackDetected();
                    _logger.LogWarning(
                        "Distributed attack pattern detected. HashedEmail={HashedEmail}, IP={IpAddress}",
                        IdentifierHasher.HashShort(request.Email), ipAddress);
                    break;
            }

            IdentityTelemetry.RecordLoginFailure(validation.BlockReason?.ToString() ?? "blocked");

            // Return generic error message to prevent account enumeration
            return Result<LoginResponse>.Failure(
                new Error("Auth.TooManyAttempts", validation.Message ?? "Too many login attempts. Please try again later"));
        }

        // Step 2: Perform constant-time user lookup and password validation
        // This section is designed to take approximately the same time regardless of whether user exists
        var user = await _userManager.FindByEmailAsync(request.Email);

        bool isPasswordValid = false;
        bool isLockedOut = false;
        string? failureReason = null;

        if (user != null)
        {
            // User exists - perform real password check
            var signInResult = await _signInManager.CheckPasswordSignInAsync(
                user, 
                request.Password, 
                lockoutOnFailure: true);

            isPasswordValid = signInResult.Succeeded;
            isLockedOut = signInResult.IsLockedOut;
        }
        else
        {
            // User doesn't exist - perform dummy password hash verification
            // This prevents timing attacks that could reveal user existence
            var passwordHasher = new PasswordHasher<ApplicationUser>();
            var dummyUser = new ApplicationUser { Email = request.Email };

            const string dummyHash = "AQAAAAIAAYagAAAAEJKt5pCLQs+Y8VK0GBH5RLhHMDWNz0W9EJkWJu7gf9LVvHdFYjQYGKLVQFQVnRPW3Q==";
            passwordHasher.VerifyHashedPassword(dummyUser, dummyHash, request.Password);

            isPasswordValid = false;
            failureReason = "user_not_found";
        }

        // Step 3: Validate account state and credentials
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
            _logger.LogWarning("Login attempt for inactive/deleted user. UserId={UserId}, IsActive={IsActive}, IsDeleted={IsDeleted}",
                user.Id, user.IsActive, user.IsDeleted);
        }
        else if (isLockedOut || await _userManager.IsLockedOutAsync(user))
        {
            canProceed = false;
            failureReason = "account_locked";
            _logger.LogWarning("Login attempt for locked account. UserId={UserId}, LockoutEnd={LockoutEnd}",
                user.Id, user.LockoutEnd);
        }
        else if (!isPasswordValid)
        {
            canProceed = false;
            failureReason = "invalid_password";
            _logger.LogWarning("Invalid password attempt. UserId={UserId}, IP={IpAddress}",
                user.Id, request.IpAddress);
        }

        // Step 4: Handle failed login attempts
        if (!canProceed)
        {
            // Record failed attempt for brute-force tracking
            await _loginAttemptTracker.RecordFailedAttemptAsync(
                request.Email,
                ipAddress,
                cancellationToken);

            IdentityTelemetry.RecordLoginFailure(failureReason ?? "unknown");

            // Return uniform error message to prevent account enumeration
            // Don't reveal whether user exists, account is locked, or password is wrong
            var errorMessage = failureReason switch
            {
                "account_locked" => "Your account is locked. Please try again later",
                "account_disabled" => "Your account has been disabled. Please contact support",
                _ => "Invalid email or password"
            };

            var errorCode = failureReason switch
            {
                "account_locked" => "Auth.AccountLocked",
                "account_disabled" => "Auth.AccountDisabled",
                _ => "Auth.InvalidCredentials"
            };

            return Result<LoginResponse>.Failure(new Error(errorCode, errorMessage));
        }

        // Step 5: Handle 2FA if enabled
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
                // Record failed attempt for invalid 2FA
                await _loginAttemptTracker.RecordFailedAttemptAsync(
                    request.Email,
                    ipAddress,
                    cancellationToken);

                _logger.LogWarning("Invalid 2FA code. UserId={UserId}", user.Id);
                IdentityTelemetry.RecordLoginFailure("invalid_2fa");
                return Result<LoginResponse>.Failure(new Error("Auth.Invalid2FA", "Invalid two-factor code"));
            }
        }

        // Step 6: Generate tokens and complete successful login
        var accessToken = await _tokenService.GenerateAccessTokenAsync(user, cancellationToken);
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user.Id, request.IpAddress ?? "unknown", cancellationToken);

        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = request.IpAddress;
        await _userManager.UpdateAsync(user);

        // Clear failed attempt tracking on successful login
        await _loginAttemptTracker.RecordSuccessfulLoginAsync(request.Email, cancellationToken);

        var roles = await _userManager.GetRolesAsync(user);

        _logger.LogInformation("User logged in successfully. UserId={UserId}, IP={IpAddress}",
            user.Id, request.IpAddress);
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
