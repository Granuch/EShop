using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Auth.Queries.GetUserByEmail;

/// <summary>
/// Query to get user by email
/// </summary>
public record GetUserByEmailQuery : IRequest<Result<UserByEmailResponse>>
{
    public string Email { get; init; } = string.Empty;
}

public record UserByEmailResponse
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public bool EmailConfirmed { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}
