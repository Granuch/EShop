using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetUserContact;

public record GetUserContactQuery : IRequest<Result<UserContactResponse>>
{
    public string UserId { get; init; } = string.Empty;
}

public record UserContactResponse
{
    /// <summary>
    /// DEBT-18. Named <c>Id</c>, not <c>UserId</c>. This was the odd one out: <c>UserProfileResponse</c>
    /// and the login response's <c>UserDto</c> both call the same value <c>Id</c>, so a client
    /// consuming more than one of them had to special-case this endpoint. Safe to rename because
    /// the endpoint is InternalService-only and its sole consumer — Notification's
    /// <c>UserContactResolver</c> — reads only the email and name fields; it is also absent from
    /// `docs/01-overview/Data Contracts.md`, so no published contract described the old name.
    /// </summary>
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}
