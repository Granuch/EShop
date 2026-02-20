using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Domain;

namespace EShop.Identity.Application.Auth.Commands.RevokeToken;

/// <summary>
/// Command to revoke a refresh token (logout)
/// </summary>
public record RevokeTokenCommand : IRequest<Result<bool>>, ITransactionalCommand
{
    [SensitiveData]
    public string RefreshToken { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
}
