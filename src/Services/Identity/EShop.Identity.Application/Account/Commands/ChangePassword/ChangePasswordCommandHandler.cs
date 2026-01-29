using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Account.Commands.ChangePassword;

/// <summary>
/// Handler for changing user password
/// </summary>
public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result<ChangePasswordResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<ChangePasswordResponse>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null)
        {
            return Result<ChangePasswordResponse>.Failure(new Error("Account.NotFound", "User not found"));
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to change password for user {UserId}: {Errors}", request.UserId, errors);
            return Result<ChangePasswordResponse>.Failure(new Error("Account.PasswordChangeFailed", errors));
        }

        _logger.LogInformation("Password changed for user {UserId}", request.UserId);

        return Result<ChangePasswordResponse>.Success(new ChangePasswordResponse
        {
            Message = "Password changed successfully"
        });
    }
}
