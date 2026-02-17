using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Domain;

namespace EShop.Identity.Application.Auth.Commands.ResetPassword;

/// <summary>
/// Command to reset password with token
/// </summary>
public record ResetPasswordCommand : IRequest<Result<ResetPasswordResponse>>, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
    [SensitiveData]
    public string Token { get; init; } = string.Empty;
    [SensitiveData]
    public string NewPassword { get; init; } = string.Empty;
}

public record ResetPasswordResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
