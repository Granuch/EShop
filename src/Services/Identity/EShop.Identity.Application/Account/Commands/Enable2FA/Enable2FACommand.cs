using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;

namespace EShop.Identity.Application.Account.Commands.Enable2FA;

/// <summary>
/// Command to begin two-factor enrolment: returns the shared key and QR code URI.
/// It does NOT arm 2FA — TwoFactorEnabled is only flipped by Verify2FACommand, which is
/// therefore the command that carries ICacheInvalidatingCommand. Nothing cached changes here.
/// </summary>
public record Enable2FACommand : IRequest<Result<Enable2FAResponse>>, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
}

public record Enable2FAResponse
{
    public string SharedKey { get; init; } = string.Empty;
    public string QrCodeUri { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
