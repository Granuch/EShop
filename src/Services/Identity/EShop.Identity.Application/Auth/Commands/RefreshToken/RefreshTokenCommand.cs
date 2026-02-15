using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;

namespace EShop.Identity.Application.Auth.Commands.RefreshToken;

/// <summary>
/// Command to refresh access token using refresh token
/// </summary>
public record RefreshTokenCommand : IRequest<Result<RefreshTokenResponse>>
{
    [SensitiveData]
    public string RefreshToken { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
}

public record RefreshTokenResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
}
