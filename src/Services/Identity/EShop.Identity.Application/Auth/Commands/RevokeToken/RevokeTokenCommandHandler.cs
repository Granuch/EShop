using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.RevokeToken;

/// <summary>
/// Handler for revoking refresh token
/// </summary>
public class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, Result<bool>>
{
    private readonly ITokenService _tokenService;
    private readonly ILogger<RevokeTokenCommandHandler> _logger;

    public RevokeTokenCommandHandler(
        ITokenService tokenService,
        ILogger<RevokeTokenCommandHandler> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(RevokeTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return Result<bool>.Failure(new Error("Auth.InvalidToken", "Refresh token is required"));
        }

        await _tokenService.RevokeTokenAsync(
            request.RefreshToken, 
            request.IpAddress ?? "unknown", 
            cancellationToken);

        _logger.LogInformation("Token revoked successfully");

        return Result<bool>.Success(true);
    }
}
