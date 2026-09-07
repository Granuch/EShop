using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Identity.Application.Account.Commands.Verify2FA;

/// <summary>
/// Command to verify and enable two-factor authentication.
/// This is the command that actually flips TwoFactorEnabled (via SetTwoFactorEnabledAsync),
/// so it is the one that must invalidate the cached profile.
/// </summary>
public record Verify2FACommand : IRequest<Result<Verify2FAResponse>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Invalidates the user's profile cache once 2FA is actually enabled.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public record Verify2FAResponse
{
    public bool Success { get; init; }
    public string[] RecoveryCodes { get; init; } = [];
    public string Message { get; init; } = string.Empty;
}
