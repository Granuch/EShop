using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Identity.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.DeleteUser;

/// <summary>
/// Soft-deletes an account (Admin panel S7, endpoint #8).
/// </summary>
/// <remarks>
/// <para>
/// Goes through <c>IUserRepository.DeleteAsync</c>, which was the only coherent retirement path in
/// the service and — until this stage — <b>had no caller at all</b>. It calls
/// <c>ApplicationUser.SoftDelete()</c>, so <c>IsDeleted</c>, <c>DeletedAt</c> and <c>IsActive</c>
/// move together, and it throws rather than reporting a failed <c>IdentityResult</c>, which is
/// what lets <c>TransactionBehavior</c> roll the whole command back.
/// </para>
/// <para>
/// Sessions are revoked for the same reason as deactivation, and here it matters more: the account
/// disappears behind the global query filter, so nothing would ever look at its refresh tokens
/// again and they would sit on the table until the cleanup job expired them.
/// </para>
/// </remarks>
public record DeleteUserCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, Result<Unit>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        // The filtered lookup, deliberately: deleting an already-deleted account is a not-found,
        // not a second delete that would move DeletedAt.
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        await _userRepository.DeleteAsync(user, cancellationToken);

        await _refreshTokenRepository.RevokeAllUserTokensAsync(
            user.Id,
            "Account deleted by an administrator",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Admin soft-deleted an account. UserId={UserId}", request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
