using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Auth.Commands.Login;

/// <summary>
/// Handler for user login
/// </summary>
public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    // TODO: Inject UserManager, SignInManager, ITokenService, ILogger
    // private readonly UserManager<ApplicationUser> _userManager;
    // private readonly SignInManager<ApplicationUser> _signInManager;
    // private readonly ITokenService _tokenService;

    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // TODO: Find user by email
        // TODO: Check if user is active and not deleted
        // TODO: Verify password using SignInManager
        // TODO: Check if email is confirmed
        // TODO: Handle account lockout after failed attempts
        // TODO: If 2FA is enabled, verify code or return Requires2FA=true
        // TODO: Generate access token and refresh token
        // TODO: Update LastLoginAt and LastLoginIp
        // TODO: Return tokens and user info
        throw new NotImplementedException();
    }
}
