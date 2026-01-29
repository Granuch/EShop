using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Auth.Commands.ConfirmEmail;

/// <summary>
/// Handler for email confirmation
/// </summary>
public class ConfirmEmailCommandHandler : IRequestHandler<ConfirmEmailCommand, Result<ConfirmEmailResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ConfirmEmailCommandHandler> _logger;

    public ConfirmEmailCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<ConfirmEmailCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<ConfirmEmailResponse>> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        
        if (user == null)
        {
            _logger.LogWarning("Email confirmation attempt for non-existent user: {UserId}", request.UserId);
            return Result<ConfirmEmailResponse>.Failure(new Error("Auth.UserNotFound", "User not found"));
        }

        if (user.EmailConfirmed)
        {
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
            _logger.LogWarning("Email confirmation failed for user {UserId}: {Errors}", user.Id, errors);
            return Result<ConfirmEmailResponse>.Failure(new Error("Auth.InvalidToken", "Invalid or expired confirmation token"));
        }

        _logger.LogInformation("Email confirmed for user: {UserId}", user.Id);

        return Result<ConfirmEmailResponse>.Success(new ConfirmEmailResponse
        {
            Success = true,
            Message = "Email confirmed successfully"
        });
    }
}
