using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Auth.Commands.Login;

/// <summary>
/// Command to authenticate user and get tokens
/// </summary>
public record LoginCommand : IRequest<Result<LoginResponse>>
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? TwoFactorCode { get; init; }
    public string? IpAddress { get; init; }
}

public record LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public bool Requires2FA { get; init; }
    public UserDto? User { get; init; }
}

public record UserDto
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public List<string> Roles { get; init; } = new();
}
