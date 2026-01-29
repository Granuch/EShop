using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Auth.Commands.RevokeToken;

/// <summary>
/// Command to revoke a refresh token (logout)
/// </summary>
public record RevokeTokenCommand : IRequest<Result<bool>>
{
    public string RefreshToken { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
}
