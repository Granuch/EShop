using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.ConfirmUserEmail;

/// <summary>
/// Marks an address confirmed without the user clicking a link (Admin panel S7, endpoint #13).
/// </summary>
/// <remarks>
/// <para>
/// The support path beside self-service confirmation: registration emails a link
/// (<c>EmailConfirmationRequestedIntegrationEvent</c>) and <c>POST /api/v1/auth/resend-confirmation</c>
/// sends another, but a user whose mailbox never receives it — or an account an administrator
/// created with <c>emailConfirmed: false</c> — still needs someone to vouch for the address before
/// they can place an order (or sign in at all, in strict mode: <c>SignIn.RequireConfirmedEmail</c>).
/// </para>
/// <para>
/// It sets the flag directly instead of minting a token and calling <c>ConfirmEmailAsync</c>. A
/// self-issued token verified by the same process proves nothing about the address, so the honest
/// spelling is "an administrator asserted this", and the log line says so.
/// </para>
/// <para>
/// Idempotent: confirming an already-confirmed address succeeds and changes nothing, which is what
/// a panel button clicked twice should do.
/// </para>
/// </remarks>
public record ConfirmUserEmailCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "User";

    string? IAuditedCommand.AuditEntityId => UserId;

    public string UserId { get; init; } = string.Empty;

    /// <summary><c>UserProfileResponse.EmailConfirmed</c> is cached, so it goes stale here.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public class ConfirmUserEmailCommandHandler : IRequestHandler<ConfirmUserEmailCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ConfirmUserEmailCommandHandler> _logger;

    public ConfirmUserEmailCommandHandler(
        UserManager<ApplicationUser> userManager,
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<ConfirmUserEmailCommandHandler> logger)
    {
        _userManager = userManager;
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(ConfirmUserEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        if (user.EmailConfirmed)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        user.EmailConfirmed = true;

        AdminUserErrors.EnsureSucceededAfterMutation(
            await _userManager.UpdateAsync(user), "Confirming the user's email");

        // The same event the self-service confirmation path publishes, so a downstream consumer
        // sees one kind of "this address is now confirmed" regardless of who asserted it. It
        // carries only the user id — the address is deliberately left out to keep PII off the bus.
        _outbox.Enqueue(new UserEmailConfirmedIntegrationEvent
        {
            UserId = user.Id,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        _logger.LogInformation(
            "An administrator asserted a user's email is confirmed; no token was verified. UserId={UserId}",
            request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
