using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;

namespace EShop.Identity.Application.Auth.Commands.ResetPassword;

/// <summary>
/// Command to reset password with token
/// </summary>
public record ResetPasswordCommand : IRequest<Result<ResetPasswordResponse>>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;
    [SensitiveData]
    public string Token { get; init; } = string.Empty;
    [SensitiveData]
    public string NewPassword { get; init; } = string.Empty;

    /// <summary>
    /// A successful reset also confirms an unconfirmed address, and the cached profile reports
    /// <c>emailConfirmed</c>. Explicit, like <c>ConfirmEmailCommand</c>'s, to stay out of the wire shape.
    /// </summary>
    IEnumerable<string> ICacheInvalidatingCommand.CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public record ResetPasswordResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
