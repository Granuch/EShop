using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Auth.Commands.RefreshToken;

/// <summary>
/// Handler for refreshing access token
/// </summary>
public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>
{
    // TODO: Inject ITokenService, UserManager, ILogger
    // private readonly ITokenService _tokenService;
    // private readonly UserManager<ApplicationUser> _userManager;

    public async Task<Result<RefreshTokenResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        // TODO: Validate refresh token
        // TODO: Extract userId from refresh token
        // TODO: Check if token is expired or revoked
        // TODO: Find user and verify account is active
        // TODO: Generate new access token
        // TODO: Generate new refresh token (token rotation)
        // TODO: Revoke old refresh token
        // TODO: Return new tokens
        throw new NotImplementedException();
    }
}
