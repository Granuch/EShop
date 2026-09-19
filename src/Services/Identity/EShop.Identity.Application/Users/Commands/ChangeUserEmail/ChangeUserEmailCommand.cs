using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.ChangeUserEmail;

/// <summary>
/// Changes a user's email address (Admin panel S7, endpoint #5).
/// </summary>
/// <remarks>
/// <para>
/// <b>The user name moves with the email.</b> Registration sets <c>UserName = Email</c> and login
/// looks the account up by email, so changing one without the other leaves an account whose login
/// name is an address that no longer exists — reachable by nothing, and invisible until someone
/// tries to sign in. ASP.NET Identity's unique index is on the <i>user name</i>, which is the other
/// reason the two cannot drift.
/// </para>
/// <para>
/// <c>markConfirmed</c> exists because the two reasons to reach this endpoint want opposite
/// answers: correcting a typo in an address the person demonstrably owns should not lock them out
/// of a confirmed account, while moving an account to a new address should not assert that the new
/// one has been verified. Defaulting to false is the safe half, so the confirmation is something an
/// admin has to ask for.
/// </para>
/// </remarks>
public record ChangeUserEmailCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// <c>[SensitiveData]</c> because an email address is personal data and
    /// <c>LoggingBehavior</c> writes the whole request object at Information. Its hardcoded
    /// redaction list covers credentials only.
    /// </summary>
    [SensitiveData]
    public string Email { get; init; } = string.Empty;

    public bool MarkConfirmed { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public class ChangeUserEmailCommandValidator : AbstractValidator<ChangeUserEmailCommand>
{
    public ChangeUserEmailCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User ID is required");

        // EmailAddress() is lenient here as everywhere in this service — it wants an '@' with no
        // surrounding whitespace, not RFC grammar. It is a usability check, not a defence.
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters")
            .EmailAddress().WithMessage("Email must be a valid email address");
    }
}

public class ChangeUserEmailCommandHandler : IRequestHandler<ChangeUserEmailCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<ChangeUserEmailCommandHandler> _logger;

    public ChangeUserEmailCommandHandler(
        UserManager<ApplicationUser> userManager,
        IUserRepository userRepository,
        ILogger<ChangeUserEmailCommandHandler> logger)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(ChangeUserEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        var email = request.Email.Trim();

        // THE pre-check of this stage, and it must stay above the assignments below.
        //
        // UserManager.UpdateAsync validates before it saves, so on a duplicate it returns a failed
        // IdentityResult having written nothing — but the entity is already mutated and tracked,
        // and TransactionBehavior commits on a Result failure. The duplicate would therefore be
        // persisted while the caller is told the request was refused. Checking first is what makes
        // the refusal real, and EmailIsTakenAsync also sees soft-deleted accounts, which still hold
        // the unique user-name index row.
        if (await _userRepository.EmailIsTakenAsync(email, excludingUserId: user.Id, cancellationToken))
        {
            return Result<Unit>.Failure(AdminUserErrors.EmailConflict);
        }

        user.Email = email;
        user.UserName = email;
        user.EmailConfirmed = request.MarkConfirmed;

        // UpdateAsync re-normalises both columns and re-runs the user validators, so the write that
        // lands is the same one self-service registration would produce.
        AdminUserErrors.EnsureSucceededAfterMutation(
            await _userManager.UpdateAsync(user), "Changing the user's email");

        _logger.LogInformation(
            "Admin changed a user's email. UserId={UserId}, MarkConfirmed={MarkConfirmed}",
            request.UserId, request.MarkConfirmed);

        return Result<Unit>.Success(Unit.Value);
    }
}
