using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.ResendEmailConfirmation;

/// <summary>
/// Enqueues a fresh <see cref="EmailConfirmationRequestedIntegrationEvent"/> for an active,
/// unconfirmed account, at most once per <see cref="IEmailConfirmationResendThrottle"/> cooldown.
///
/// <para>
/// <b>Every path returns the same success.</b> An unknown address, a disabled account, an address
/// already confirmed and a resend inside the cooldown all answer exactly as a real send does, and
/// each of them still generates a (discarded) token so the work done is comparable — the same
/// defence <c>ForgotPasswordCommandHandler</c> uses against timing enumeration.
/// <c>UniformResponseTimingMiddleware</c> pads the endpoint on top of that.
/// </para>
/// </summary>
public class ResendEmailConfirmationCommandHandler
    : IRequestHandler<ResendEmailConfirmationCommand, Result<ResendEmailConfirmationResponse>>
{
    public const string UniformMessage =
        "If the address belongs to an account awaiting confirmation, a new confirmation link has been sent";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailConfirmationResendThrottle _throttle;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ResendEmailConfirmationCommandHandler> _logger;

    public ResendEmailConfirmationCommandHandler(
        UserManager<ApplicationUser> userManager,
        IEmailConfirmationResendThrottle throttle,
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<ResendEmailConfirmationCommandHandler> logger)
    {
        _userManager = userManager;
        _throttle = throttle;
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<ResendEmailConfirmationResponse>> Handle(
        ResendEmailConfirmationCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user is null || !user.IsActive || user.EmailConfirmed)
        {
            _logger.LogInformation(
                "Confirmation resend requested for an address with nothing to confirm. HashedEmail={HashedEmail}",
                IdentifierHasher.HashShort(request.Email));
            await GenerateDummyTokenAsync(request.Email);
            return Uniform();
        }

        // The throttle is claimed only for an account that would actually be sent something, so an
        // unknown address can never start (or be told about) a cooldown.
        if (!await _throttle.TryAcquireAsync(user.Id, cancellationToken))
        {
            _logger.LogInformation("Confirmation resend refused by the cooldown. UserId={UserId}", user.Id);
            await GenerateDummyTokenAsync(request.Email);
            return Uniform();
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        _outbox.Enqueue(new EmailConfirmationRequestedIntegrationEvent
        {
            UserId = user.Id,
            ConfirmationToken = token,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        // No SaveChangesAsync: the command is ITransactionalCommand, so TransactionBehavior's commit
        // persists the outbox row.
        _logger.LogInformation("Confirmation email re-requested. UserId={UserId}", user.Id);
        return Uniform();
    }

    /// <summary>
    /// Token generation for an account that does not exist, so the refused paths cost about what the
    /// real one does. The throwaway user has a fixed security stamp; the token is never stored.
    /// </summary>
    private Task GenerateDummyTokenAsync(string email)
        => _userManager.GenerateEmailConfirmationTokenAsync(new ApplicationUser
        {
            Id = Guid.Empty.ToString(),
            Email = email,
            UserName = email,
            SecurityStamp = Guid.Empty.ToString()
        });

    private static Result<ResendEmailConfirmationResponse> Uniform()
        => Result<ResendEmailConfirmationResponse>.Success(new ResendEmailConfirmationResponse
        {
            Success = true,
            Message = UniformMessage
        });
}
