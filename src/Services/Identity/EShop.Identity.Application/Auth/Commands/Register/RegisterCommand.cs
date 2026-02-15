using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;

namespace EShop.Identity.Application.Auth.Commands.Register;

/// <summary>
/// Command to register a new user
/// </summary>
public record RegisterCommand : IRequest<Result<RegisterResponse>>
{
    public string Email { get; init; } = string.Empty;
    [SensitiveData]
    public string Password { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}

public record RegisterResponse
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
