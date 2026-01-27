using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Auth.Commands.Register;

/// <summary>
/// Handler for user registration
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, Result<RegisterResponse>>
{
    // TODO: Inject UserManager, IEmailService, ILogger
    // private readonly UserManager<ApplicationUser> _userManager;
    // private readonly IEmailService _emailService;

    public async Task<Result<RegisterResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        // TODO: Check if user already exists
        // TODO: Create ApplicationUser entity
        // TODO: Hash password and create user using UserManager
        // TODO: Assign default "User" role
        // TODO: Generate email confirmation token
        // TODO: Send confirmation email
        // TODO: Publish UserRegisteredEvent
        // TODO: Return success response
        throw new NotImplementedException();
    }
}
