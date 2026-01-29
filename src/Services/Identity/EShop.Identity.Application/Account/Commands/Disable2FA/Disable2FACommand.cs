using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Account.Commands.Disable2FA;

/// <summary>
/// Command to disable two-factor authentication
/// </summary>
public record Disable2FACommand : IRequest<Result<Disable2FAResponse>>
{
    public string UserId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

public record Disable2FAResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
