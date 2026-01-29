using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Auth.Commands.ConfirmEmail;

/// <summary>
/// Command to confirm user's email
/// </summary>
public record ConfirmEmailCommand : IRequest<Result<ConfirmEmailResponse>>
{
    public string UserId { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
}

public record ConfirmEmailResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
