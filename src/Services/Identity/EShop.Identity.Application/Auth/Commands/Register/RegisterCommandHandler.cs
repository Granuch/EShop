using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Events;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Application.Telemetry;
using EShop.Identity.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.Register;

/// <summary>
/// Handler for user registration
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, Result<RegisterResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserRepository _userRepository;
    private readonly IMediator _mediator;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        UserManager<ApplicationUser> userManager,
        IUserRepository userRepository,
        IMediator mediator,
        ILogger<RegisterCommandHandler> logger)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<Result<RegisterResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        // Check if user already exists
        var existingUser = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existingUser != null)
        {
            _logger.LogWarning("Registration attempt with existing email. HashedEmail={HashedEmail}",
                IdentifierHasher.HashShort(request.Email));
            IdentityTelemetry.RecordRegistrationFailure("email_exists");
            return Result<RegisterResponse>.Failure(new Error("Auth.EmailExists", "A user with this email already exists"));
        }

        // Create ApplicationUser entity
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            EmailConfirmed = false
        };

        // Create user with password
        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            _logger.LogError("Failed to create user. HashedEmail={HashedEmail}, Errors={Errors}",
                IdentifierHasher.HashShort(request.Email), errors);
            IdentityTelemetry.RecordRegistrationFailure("create_failed");
            return Result<RegisterResponse>.Failure(new Error("Auth.CreateFailed", errors));
        }

        // Assign default "User" role
        var roleResult = await _userManager.AddToRoleAsync(user, "User");
        if (!roleResult.Succeeded)
        {
            _logger.LogWarning("Failed to assign User role. UserId={UserId}", user.Id);
        }

        // Generate email confirmation token (for future email confirmation feature)
        var confirmationToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // Publish domain event
        await _mediator.Publish(new UserRegisteredEvent
        {
            UserId = user.Id,
            Email = user.Email!,
            FullName = user.FullName
        }, cancellationToken);

        _logger.LogInformation("User registered successfully. UserId={UserId}", user.Id);
        IdentityTelemetry.RecordRegistrationSuccess();

        return Result<RegisterResponse>.Success(new RegisterResponse
        {
            UserId = user.Id,
            Email = user.Email!,
            Message = "Registration successful. Please check your email to confirm."
        });
    }
}
