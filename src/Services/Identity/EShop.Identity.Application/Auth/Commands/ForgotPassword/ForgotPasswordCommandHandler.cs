using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Events;
using EShop.Identity.Application.Telemetry;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Handler for forgot password request
/// </summary>
public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Result<ForgotPasswordResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<ForgotPasswordResponse>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Always return success to prevent email enumeration attacks
        if (user == null || !user.IsActive || user.IsDeleted)
        {
            _logger.LogInformation("Password reset requested for non-existent or inactive email. Email={Email}", request.Email);
            IdentityTelemetry.RecordForgotPassword();
            return Result<ForgotPasswordResponse>.Success(new ForgotPasswordResponse
            {
                Success = true,
                Message = "If the email exists, a password reset link has been sent"
            });
        }

        // Generate password reset token
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        // TODO: EXTERNAL INTEGRATION REQUIRED
        // Send email with reset link using IEmailService
        // Example:
        // await _emailService.SendPasswordResetEmailAsync(user.Email!, user.Id, token);

        _logger.LogInformation("Password reset token generated. UserId={UserId}, Email={Email}", user.Id, user.Email);
        IdentityTelemetry.RecordForgotPassword();

        return Result<ForgotPasswordResponse>.Success(new ForgotPasswordResponse
        {
            Success = true,
            Message = "If the email exists, a password reset link has been sent"
        });
    }
}
