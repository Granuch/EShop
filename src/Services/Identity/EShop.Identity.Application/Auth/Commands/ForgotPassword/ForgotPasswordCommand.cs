using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;

namespace EShop.Identity.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Command to request password reset
/// </summary>
public record ForgotPasswordCommand : IRequest<Result<ForgotPasswordResponse>>, ITransactionalCommand
{
    public string Email { get; init; } = string.Empty;
}

public record ForgotPasswordResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
