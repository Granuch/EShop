using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.UnlockUser;

/// <summary>
/// Lifts a lockout (Admin panel S7, endpoint #11).
/// </summary>
/// <remarks>
/// <para>
/// <b>There are two independent lockouts in this service, and clearing only the obvious one leaves
/// the user still locked out.</b> ASP.NET Identity's <c>LockoutEnd</c>/<c>AccessFailedCount</c> are
/// what the detail card shows and what <c>UserManager.IsLockedOutAsync</c> reads — but
/// <c>LoginCommandHandler</c> never increments <c>AccessFailedCount</c>. The counter that actually
/// blocks a real brute-force victim is <c>ILoginAttemptTracker</c>'s, in Redis, keyed by a hash of
/// the email and consulted by <c>ValidateAttemptAsync</c> before the password is even checked.
/// </para>
/// <para>
/// So this clears both. <c>ILoginAttemptTracker.ResetAccountAttemptsAsync</c> was written for
/// precisely this ("used by administrators or after account verification") and until this stage had
/// <b>no caller outside a successful login</b> — an admin had no way to reach it, which meant a
/// locked-out user's only remedy was to wait out the window.
/// </para>
/// <para>
/// The tracker's reset is best-effort by nature: it lives in Redis, not in the transaction, so it
/// cannot be rolled back with the database write and an outage leaves the window to expire on its
/// own. It is awaited rather than swallowed, so a failure is still reported.
/// </para>
/// </remarks>
public record UnlockUserCommand : IRequest<Result<Unit>>, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
}

public class UnlockUserCommandHandler : IRequestHandler<UnlockUserCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILoginAttemptTracker _loginAttemptTracker;
    private readonly ILogger<UnlockUserCommandHandler> _logger;

    public UnlockUserCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILoginAttemptTracker loginAttemptTracker,
        ILogger<UnlockUserCommandHandler> logger)
    {
        _userManager = userManager;
        _loginAttemptTracker = loginAttemptTracker;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(UnlockUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;

        AdminUserErrors.EnsureSucceededAfterMutation(
            await _userManager.UpdateAsync(user), "Unlocking the account");

        if (!string.IsNullOrEmpty(user.Email))
        {
            // The tracker is keyed by the login identifier, which is the email — the same value
            // LoginCommandHandler passes to ValidateAttemptAsync.
            await _loginAttemptTracker.ResetAccountAttemptsAsync(user.Email, cancellationToken);
        }

        _logger.LogInformation("Admin unlocked an account. UserId={UserId}", request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
