using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Application.Telemetry;
using EShop.Identity.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Handler for forgot password request
/// </summary>
public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Result<ForgotPasswordResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<ForgotPasswordResponse>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Always return success to prevent email enumeration attacks
        if (user == null || !user.IsActive)
        {
            // The response body is identical either way, but the *work* was not: the found path
            // generates a reset token and writes to the outbox, the not-found path returned
            // immediately. That timing difference is itself the enumeration oracle the identical
            // message is meant to close. Do comparable work here, the same defence
            // LoginCommandHandler applies with its dummy hash verification.
            await _userManager.GeneratePasswordResetTokenAsync(new ApplicationUser
            {
                Id = Guid.Empty.ToString(),
                Email = request.Email,
                UserName = request.Email,
                SecurityStamp = Guid.Empty.ToString()
            });

            _logger.LogInformation("Password reset requested for non-existent or inactive email. HashedEmail={HashedEmail}",
                IdentifierHasher.HashShort(request.Email));
            IdentityTelemetry.RecordForgotPassword();
            return Result<ForgotPasswordResponse>.Success(new ForgotPasswordResponse
            {
                Success = true,
                Message = "If the email exists, a password reset link has been sent"
            });
        }

        // Generate password reset token
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        _outbox.Enqueue(new PasswordResetRequestedIntegrationEvent
        {
            UserId = user.Id,
            ResetToken = token,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        // No SaveChangesAsync here: the command is ITransactionalCommand now, so
        // TransactionBehavior's CommitTransactionAsync is what persists the outbox row.
        _logger.LogInformation("Password reset token generated. UserId={UserId}", user.Id);
        IdentityTelemetry.RecordForgotPassword();

        return Result<ForgotPasswordResponse>.Success(new ForgotPasswordResponse
        {
            Success = true,
            Message = "If the email exists, a password reset link has been sent"
        });
    }
}
