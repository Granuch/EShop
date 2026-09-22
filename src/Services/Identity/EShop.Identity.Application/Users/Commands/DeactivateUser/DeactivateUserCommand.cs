using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.DeactivateUser;

/// <summary>
/// Suspends an account without deleting it (Admin panel S7, endpoint #7).
/// </summary>
/// <remarks>
/// <para>
/// <b>It also revokes every refresh token, and that is part of the operation rather than a
/// follow-up.</b> <c>RefreshTokenCommandHandler</c> already refuses a disabled account, so the
/// suspended user cannot mint a new access token — but their existing refresh tokens stay on the
/// table looking live, and a re-activation hours later silently hands their old sessions back.
/// Revoking now means re-enabling the account is a clean sign-in, and it bounds the existing access
/// token's remaining hour rather than leaving anything open-ended.
/// </para>
/// <para>
/// The revoke has no <c>try/catch</c> and no <c>SaveChangesAsync</c> on purpose. Under
/// <c>ITransactionalCommand</c> that is what makes the pair atomic: if it throws, the deactivation
/// rolls back and the caller is told, instead of a 200 that suspended an account while leaving its
/// sessions alive. Same shape as <c>ChangePasswordCommandHandler</c> (SEC-06) — the absence of
/// error handling is the feature.
/// </para>
/// </remarks>
public record DeactivateUserCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "User";

    string? IAuditedCommand.AuditEntityId => UserId;

    public string UserId { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ILogger<DeactivateUserCommandHandler> _logger;

    public DeactivateUserCommandHandler(
        UserManager<ApplicationUser> userManager,
        IRefreshTokenRepository refreshTokenRepository,
        ILogger<DeactivateUserCommandHandler> logger)
    {
        _userManager = userManager;
        _refreshTokenRepository = refreshTokenRepository;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        user.Deactivate();

        AdminUserErrors.EnsureSucceededAfterMutation(
            await _userManager.UpdateAsync(user), "Deactivating the account");

        await _refreshTokenRepository.RevokeAllUserTokensAsync(
            user.Id,
            "Account deactivated by an administrator",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Admin deactivated an account. UserId={UserId}", request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
