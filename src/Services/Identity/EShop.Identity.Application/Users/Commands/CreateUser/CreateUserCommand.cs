using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Application.Validation;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.CreateUser;

/// <summary>
/// Creates an account on an admin's behalf (Admin panel S7, endpoint #3).
/// </summary>
/// <remarks>
/// <para>
/// <b>The password is optional, and omitting it is the normal case.</b> With no password the
/// account is created without one and a password-reset link is emailed, through the
/// <c>PasswordResetRequestedIntegrationEvent</c> that <c>ForgotPassword</c> already publishes and
/// Notification already consumes — so "invite a colleague" costs no new event, no new template and
/// no new consumer. That is also what decision Q2a asks for on the reset endpoint: an admin never
/// learns a password they did not choose, because there is nothing to learn.
/// </para>
/// <para>
/// There is deliberately <b>no separate <c>sendInvite</c> flag</b>. With one, three of the four
/// combinations are reachable and one of them — no password and no invite — creates an account
/// nobody can ever sign into, silently. Deriving the invite from the absence of a password leaves
/// no such state.
/// </para>
/// </remarks>
public record CreateUserCommand : IRequest<Result<CreateUserResponse>>, ITransactionalCommand
{
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Optional. When supplied, the account is usable immediately; when omitted, the account is
    /// created with no password and a reset link is emailed instead.
    /// </summary>
    /// <remarks>
    /// <c>[SensitiveData]</c> rather than relying on <c>LoggingBehavior</c>'s hardcoded name list.
    /// The list happens to contain "Password" today, which makes the attribute look redundant — it
    /// is not: the list is a coincidence, not a contract, and renaming this property to
    /// <c>InitialPassword</c> would start logging the value in cleartext at Information with
    /// nothing failing.
    /// </remarks>
    [SensitiveData]
    public string? Password { get; init; }

    /// <summary>
    /// Three-state, following the BUG-09 rule: omitted (<c>null</c>) grants the default
    /// <c>User</c> role, matching self-registration; an explicit empty array grants none; a list
    /// grants exactly those.
    /// </summary>
    public IReadOnlyList<string>? Roles { get; init; }

    /// <summary>
    /// Whether to treat the address as already verified. Defaults to false, so an admin-created
    /// account is in the same state a self-registered one is.
    /// </summary>
    public bool EmailConfirmed { get; init; }
}

public sealed record CreateUserResponse
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;

    /// <summary>True when no password was supplied and a reset link was emailed instead.</summary>
    public bool InviteSent { get; init; }
}

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters")
            .EmailAddress().WithMessage("Email must be a valid email address");

        // Shared with RegisterCommandValidator and UpdateProfileCommandValidator: a name an admin
        // can create must also be one the user can later edit.
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(PersonNameRules.MaxLength)
                .WithMessage($"First name must not exceed {PersonNameRules.MaxLength} characters")
            .Matches(PersonNameRules.Pattern).WithMessage($"First name {PersonNameRules.Message}");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required")
            .MaximumLength(PersonNameRules.MaxLength)
                .WithMessage($"Last name must not exceed {PersonNameRules.MaxLength} characters")
            .Matches(PersonNameRules.Pattern).WithMessage($"Last name {PersonNameRules.Message}");

        // The trailing .When guards the whole chain, which is what makes the field optional.
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(32).WithMessage("Phone number must not exceed 32 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

        // Length only. Complexity belongs to IdentityOptions.Password, which UserManager enforces
        // and reports through IdentityResult — duplicating those rules here is how the two drift.
        RuleFor(x => x.Password)
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters")
            .When(x => x.Password is not null);

        RuleFor(x => x.Roles)
            .Must(roles => roles!.Count <= 10).WithMessage("A user may not be given more than 10 roles")
            .Must(roles => roles!.All(r => !string.IsNullOrWhiteSpace(r)))
                .WithMessage("Role names must not be blank")
            .When(x => x.Roles is not null);
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Result<CreateUserResponse>>
{
    private const string DefaultRole = "User";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IUserRepository _userRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IUserRepository userRepository,
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<CreateUserCommandHandler> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _userRepository = userRepository;
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<CreateUserResponse>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();

        // Both pre-checks run before anything is created, because TransactionBehavior commits on a
        // Result failure: an unknown role discovered after CreateAsync would leave a half-built
        // account behind while the caller is told the request failed.
        //
        // EmailIsTakenAsync rather than FindByEmailAsync: a soft-deleted account still holds the
        // unique NormalizedUserName index row, so a recycled address reads as free and then fails
        // at the database as a 500.
        if (await _userRepository.EmailIsTakenAsync(email, cancellationToken: cancellationToken))
        {
            return Result<CreateUserResponse>.Failure(AdminUserErrors.EmailConflict);
        }

        IReadOnlyList<string> roles = request.Roles ?? [DefaultRole];
        foreach (var role in roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                return Result<CreateUserResponse>.Failure(AdminUserErrors.RoleNotFound);
            }
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = request.EmailConfirmed
        };

        var sendInvite = string.IsNullOrEmpty(request.Password);

        // CreateAsync validates before it writes, so a rejection here leaves nothing tracked and a
        // Result failure is safe. The password overload is only used when there is one to set:
        // CreateAsync(user) leaves PasswordHash null, which is exactly what "invite pending" means.
        var createResult = sendInvite
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, request.Password!);

        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            _logger.LogWarning("Admin user creation rejected. Errors={Errors}", errors);
            return Result<CreateUserResponse>.Failure(AdminUserErrors.CreateFailed(errors));
        }

        if (roles.Count > 0)
        {
            AdminUserErrors.EnsureSucceededAfterMutation(
                await _userManager.AddToRolesAsync(user, roles), "Assigning roles to the new user");
        }

        if (sendInvite)
        {
            // The same event ForgotPassword publishes, so the existing Notification consumer and
            // template deliver it. It is ISensitivePayloadEvent, so the live token stops being
            // readable in outbox_messages the moment the row goes terminal (SEC-05).
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            _outbox.Enqueue(new PasswordResetRequestedIntegrationEvent
            {
                UserId = user.Id,
                ResetToken = token,
                CorrelationId = _currentUserContext.CorrelationId
            }, _currentUserContext.CorrelationId);
        }

        _logger.LogInformation(
            "Admin created user. UserId={UserId}, Roles={Roles}, InviteSent={InviteSent}",
            user.Id, string.Join(",", roles), sendInvite);

        return Result<CreateUserResponse>.Success(new CreateUserResponse
        {
            UserId = user.Id,
            Email = user.Email!,
            InviteSent = sendInvite
        });
    }
}
