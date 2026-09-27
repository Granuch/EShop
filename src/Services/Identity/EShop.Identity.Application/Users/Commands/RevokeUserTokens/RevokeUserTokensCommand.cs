using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Identity.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.RevokeUserTokens;

/// <summary>
/// Ends every session a user has (Admin panel S7, endpoint #17).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this does and does not stop.</b> It revokes every refresh token, so the user cannot
/// obtain a new access token — but an access token already issued stays valid until it expires,
/// which is up to an hour (<c>expiresIn</c> is 3600). Nothing in this service can shorten that:
/// the JWT is verified by signature in six other services with no revocation list. So "kill all
/// sessions" means "no renewals from now, everything gone within the hour". If an account must
/// stop acting immediately, that is <c>deactivate</c> — every service revalidates nothing, but
/// Identity refuses the refresh and the panel shows the account as suspended.
/// </para>
/// <para>
/// Uses the <b>deleted-hiding</b> lookup deliberately: a deleted account already had its tokens
/// revoked by <c>DeleteUserCommand</c>, so there is nothing here for it to do and "no such user"
/// is the honest answer.
/// </para>
/// </remarks>
public record RevokeUserTokensCommand : IRequest<Result<Unit>>, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "User";

    string? IAuditedCommand.AuditEntityId => UserId;

    public string UserId { get; init; } = string.Empty;
}

public class RevokeUserTokensCommandHandler : IRequestHandler<RevokeUserTokensCommand, Result<Unit>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ILogger<RevokeUserTokensCommandHandler> _logger;

    public RevokeUserTokensCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ILogger<RevokeUserTokensCommandHandler> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(RevokeUserTokensCommand request, CancellationToken cancellationToken)
    {
        // Existence is checked first so an unknown id is a 404 rather than a cheerful 204 over an
        // UPDATE that matched no rows.
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        await _refreshTokenRepository.RevokeAllUserTokensAsync(
            user.Id,
            "Revoked by an administrator",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Admin revoked every refresh token for a user. UserId={UserId}", request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
