using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Identity.Application.Account.Commands.Enable2FA;

/// <summary>
/// Command to enable two-factor authentication.
/// Invalidates the user's profile cache after enabling 2FA.
/// </summary>
public record Enable2FACommand : IRequest<Result<Enable2FAResponse>>, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Invalidates the user's profile cache after 2FA is enabled.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public record Enable2FAResponse
{
    public string SharedKey { get; init; } = string.Empty;
    public string QrCodeUri { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
