using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Identity.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.LockUser;

/// <summary>
/// Locks an account out until a given moment (Admin panel S7, endpoint #10).
/// </summary>
/// <remarks>
/// <para>
/// <b><c>until</c> is required, and there is deliberately no "lock indefinitely".</b> An unbounded
/// lock is what <c>deactivate</c> already models, with its own flag, its own list filter and its
/// own login response (<c>Auth.AccountDisabled</c>). Offering a second spelling of it here would
/// give the panel two buttons that mean the same thing while reading back differently.
/// </para>
/// <para>
/// <c>reason</c> is logged, not stored — there is no column for it and inventing one is the audit
/// log, which is S15. A later reader should not mistake its absence from the detail card for a
/// mapping bug.
/// </para>
/// <para>
/// Not <c>ICacheInvalidatingCommand</c>: <c>UserProfileResponse</c> carries no lockout field, so
/// there is nothing stale in <c>profile:{id}</c>. Declaring the key anyway would evict a live entry
/// for no reason and, worse, would suggest the cache tracks lockout state.
/// </para>
/// </remarks>
public record LockUserCommand : IRequest<Result<Unit>>, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
    public DateTimeOffset Until { get; init; }
    public string? Reason { get; init; }
}

public class LockUserCommandValidator : AbstractValidator<LockUserCommand>
{
    public LockUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Until)
            .Must(until => until > DateTimeOffset.UtcNow)
            .WithMessage("'until' must be in the future — a past date is an unlock, not a lock");

        RuleFor(x => x.Reason)
            .MaximumLength(250).WithMessage("Reason must not exceed 250 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Reason));
    }
}

public class LockUserCommandHandler : IRequestHandler<LockUserCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LockUserCommandHandler> _logger;

    public LockUserCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<LockUserCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(LockUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        // Both properties then ONE UpdateAsync, rather than SetLockoutEnabledAsync followed by
        // SetLockoutEndDateAsync. Those are two saves and two ConcurrencyStamp rotations for one
        // logical change — and SetLockoutEndDateAsync refuses outright when LockoutEnabled is
        // false, which would make this fail for any account created before
        // Lockout.AllowedForNewUsers was turned on.
        user.LockoutEnabled = true;
        user.LockoutEnd = request.Until;

        AdminUserErrors.EnsureSucceededAfterMutation(
            await _userManager.UpdateAsync(user), "Locking the account");

        _logger.LogInformation(
            "Admin locked an account. UserId={UserId}, Until={Until}, Reason={Reason}",
            request.UserId, request.Until, request.Reason ?? "(none given)");

        return Result<Unit>.Success(Unit.Value);
    }
}
