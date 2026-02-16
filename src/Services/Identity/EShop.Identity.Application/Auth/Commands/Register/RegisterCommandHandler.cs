using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Events;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Application.Telemetry;
using EShop.Identity.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.Register;

/// <summary>
/// Handler for user registration.
/// Wraps user creation and integration event publishing in a single transaction
/// to prevent the dual-write problem.
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, Result<RegisterResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserRepository _userRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        UserManager<ApplicationUser> userManager,
        IUserRepository userRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        ICurrentUserContext currentUserContext,
        ILogger<RegisterCommandHandler> logger)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _currentUserContext = currentUserContext;
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

        // Wrap user creation + outbox enqueue in a single transaction to prevent dual-write
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Create user with password (UserManager internally calls SaveChangesAsync,
            // but it participates in our transaction)
            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
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

            // Enqueue integration event in the same transaction — atomic with user creation
            _outbox.Enqueue(new UserRegisteredIntegrationEvent
            {
                UserId = user.Id,
                CorrelationId = _currentUserContext.CorrelationId
            }, _currentUserContext.CorrelationId);

            // Commit user + outbox message atomically
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

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
