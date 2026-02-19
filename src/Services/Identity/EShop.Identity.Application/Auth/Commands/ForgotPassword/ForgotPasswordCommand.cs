using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Command to request password reset.
/// Not transactional — this is a read-only lookup + token generation flow.
/// </summary>
public record ForgotPasswordCommand : IRequest<Result<ForgotPasswordResponse>>
{
    public string Email { get; init; } = string.Empty;
}

public record ForgotPasswordResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
