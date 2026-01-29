using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Account.Commands.ChangePassword;

/// <summary>
/// Command to change user password
/// </summary>
public record ChangePasswordCommand : IRequest<Result<ChangePasswordResponse>>
{
    public string UserId { get; init; } = string.Empty;
    public string CurrentPassword { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

public record ChangePasswordResponse
{
    public string Message { get; init; } = string.Empty;
}
