using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Identity.Application.Account.Commands.Disable2FA;

/// <summary>
/// Command to disable two-factor authentication.
/// Invalidates the user's profile cache after disabling 2FA.
/// </summary>
public record Disable2FACommand : IRequest<Result<Disable2FAResponse>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Invalidates the user's profile cache after 2FA is disabled.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public record Disable2FAResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
