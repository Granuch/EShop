using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetUserContact;

public record GetUserContactQuery : IRequest<Result<UserContactResponse>>
{
    public string UserId { get; init; } = string.Empty;
}

public record UserContactResponse
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}
