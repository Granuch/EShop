using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Identity.Application.Auth.Commands.ConfirmEmail;

/// <summary>
/// Command to confirm user's email
/// </summary>
public record ConfirmEmailCommand : IRequest<Result<ConfirmEmailResponse>>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;
    [SensitiveData]
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// The cached profile reports <c>emailConfirmed</c>, so it goes stale here — the same key the
    /// admin <c>ConfirmUserEmailCommand</c> has always evicted. Explicit, because this command is
    /// bound straight from the request body: a public member would show up in the OpenAPI schema.
    /// </summary>
    IEnumerable<string> ICacheInvalidatingCommand.CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public record ConfirmEmailResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
