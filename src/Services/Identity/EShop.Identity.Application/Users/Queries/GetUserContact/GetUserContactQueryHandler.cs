using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Interfaces;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetUserContact;

public sealed class GetUserContactQueryHandler : IRequestHandler<GetUserContactQuery, Result<UserContactResponse>>
{
    private readonly IUserRepository _userRepository;

    public GetUserContactQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Result<UserContactResponse>> Handle(GetUserContactQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user is null || user.IsDeleted || !user.IsActive || string.IsNullOrWhiteSpace(user.Email))
        {
            return Result<UserContactResponse>.Failure(
                new Error("Users.ContactNotFound", "User contact not found"));
        }

        return Result<UserContactResponse>.Success(new UserContactResponse
        {
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName
        });
    }
}
