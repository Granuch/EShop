using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Events;
using EShop.Identity.Application.Telemetry;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.ConfirmEmail;

/// <summary>
/// Handler for email confirmation
/// </summary>
public class ConfirmEmailCommandHandler : IRequestHandler<ConfirmEmailCommand, Result<ConfirmEmailResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMediator _mediator;
    private readonly ILogger<ConfirmEmailCommandHandler> _logger;

    public ConfirmEmailCommandHandler(
        UserManager<ApplicationUser> userManager,
        IMediator mediator,
        ILogger<ConfirmEmailCommandHandler> logger)
    {
        _userManager = userManager;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<Result<ConfirmEmailResponse>> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null)
        {
            _logger.LogWarning("Email confirmation attempt for non-existent user. UserId={UserId}", request.UserId);
            IdentityTelemetry.RecordEmailConfirmation(false);
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

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Email confirmation failed. UserId={UserId}, Errors={Errors}", user.Id, errors);
            IdentityTelemetry.RecordEmailConfirmation(false);
            return Result<ConfirmEmailResponse>.Failure(new Error("Auth.InvalidToken", "Invalid or expired confirmation token"));
        }

        // Publish domain event
        await _mediator.Publish(new UserEmailConfirmedEvent
        {
            UserId = user.Id,
            Email = user.Email!
        }, cancellationToken);

        _logger.LogInformation("Email confirmed successfully. UserId={UserId}, Email={Email}", user.Id, user.Email);
        IdentityTelemetry.RecordEmailConfirmation(true);

        return Result<ConfirmEmailResponse>.Success(new ConfirmEmailResponse
        {
            Success = true,
            Message = "Email confirmed successfully"
        });
    }
}
