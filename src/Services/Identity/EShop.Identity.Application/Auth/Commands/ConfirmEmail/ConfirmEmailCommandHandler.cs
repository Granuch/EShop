using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Application.Telemetry;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.ConfirmEmail;

/// <summary>
/// Handler for email confirmation.
/// Wraps email confirmation and integration event publishing in a single transaction.
/// </summary>
public class ConfirmEmailCommandHandler : IRequestHandler<ConfirmEmailCommand, Result<ConfirmEmailResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ConfirmEmailCommandHandler> _logger;

    public ConfirmEmailCommandHandler(
        UserManager<ApplicationUser> userManager,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        ICurrentUserContext currentUserContext,
        ILogger<ConfirmEmailCommandHandler> logger)
    {
        _userManager = userManager;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<ConfirmEmailResponse>> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        using var activity = IdentityActivitySource.Source.StartActivity("Identity.ConfirmEmail");
        activity?.SetTag("user.id", request.UserId);

        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null)
        {
            _logger.LogWarning("Email confirmation attempt for non-existent user. UserId={UserId}", request.UserId);
            IdentityTelemetry.RecordEmailConfirmation(false);
            activity?.SetStatus(ActivityStatusCode.Error, "user_not_found");
            return Result<ConfirmEmailResponse>.Failure(new Error("Auth.UserNotFound", "User not found"));
        }

        if (user.EmailConfirmed)
        {
            _logger.LogInformation("Email already confirmed. UserId={UserId}", user.Id);
            return Result<ConfirmEmailResponse>.Success(new ConfirmEmailResponse
            {
                Success = true,
                Message = "Email already confirmed"
            });
        }

        // Wrap confirmation + outbox enqueue in a single transaction
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await _userManager.ConfirmEmailAsync(user, request.Token);

            if (!result.Succeeded)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogWarning("Email confirmation failed. UserId={UserId}, Errors={Errors}", user.Id, errors);
                IdentityTelemetry.RecordEmailConfirmation(false);
                activity?.SetStatus(ActivityStatusCode.Error, "invalid_token");
                return Result<ConfirmEmailResponse>.Failure(new Error("Auth.InvalidToken", "Invalid or expired confirmation token"));
            }

            // Enqueue integration event in the same transaction
            _outbox.Enqueue(new UserEmailConfirmedIntegrationEvent
            {
                UserId = user.Id,
                CorrelationId = _currentUserContext.CorrelationId
            }, _currentUserContext.CorrelationId);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation("Email confirmed successfully. UserId={UserId}", user.Id);
        IdentityTelemetry.RecordEmailConfirmation(true);

        return Result<ConfirmEmailResponse>.Success(new ConfirmEmailResponse
        {
            Success = true,
            Message = "Email confirmed successfully"
        });
    }
}
