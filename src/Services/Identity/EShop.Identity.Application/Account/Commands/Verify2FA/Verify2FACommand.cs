using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;

namespace EShop.Identity.Application.Account.Commands.Verify2FA;

/// <summary>
/// Command to verify and enable two-factor authentication
/// </summary>
public record Verify2FACommand : IRequest<Result<Verify2FAResponse>>, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

public record Verify2FAResponse
{
    public bool Success { get; init; }
    public string[] RecoveryCodes { get; init; } = [];
    public string Message { get; init; } = string.Empty;
}
