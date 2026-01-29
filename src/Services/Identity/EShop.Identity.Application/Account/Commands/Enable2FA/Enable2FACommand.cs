using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Account.Commands.Enable2FA;

/// <summary>
/// Command to enable two-factor authentication
/// </summary>
public record Enable2FACommand : IRequest<Result<Enable2FAResponse>>
{
    public string UserId { get; init; } = string.Empty;
}

public record Enable2FAResponse
{
    public string SharedKey { get; init; } = string.Empty;
    public string QrCodeUri { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
