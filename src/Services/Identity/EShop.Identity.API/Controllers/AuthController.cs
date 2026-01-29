using Microsoft.AspNetCore.Mvc;
using MediatR;
using EShop.Identity.Application.Auth.Commands.Register;
using EShop.Identity.Application.Auth.Commands.Login;
using EShop.Identity.Application.Auth.Commands.RefreshToken;
using EShop.Identity.Application.Auth.Commands.RevokeToken;
using EShop.Identity.Application.Auth.Commands.ConfirmEmail;
using EShop.Identity.Application.Auth.Commands.ForgotPassword;
using EShop.Identity.Application.Auth.Commands.ResetPassword;

namespace EShop.Identity.API.Controllers;

/// <summary>
/// Authentication controller - handles login, register, token refresh
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IMediator mediator, ILogger<AuthController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error!.Code, message = result.Error.Message });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Login user and get tokens
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginCommand command)
    {
        // Add IP address to command
        var commandWithIp = command with { IpAddress = GetClientIpAddress() };
        var result = await _mediator.Send(commandWithIp);

        if (result.IsFailure)
        {
            var error = result.Error!;
            return error.Code switch
            {
                "Auth.InvalidCredentials" => Unauthorized(new { error = error.Code, message = error.Message }),
                "Auth.AccountLocked" => StatusCode(StatusCodes.Status423Locked, new { error = error.Code, message = error.Message }),
                "Auth.AccountDisabled" => BadRequest(new { error = error.Code, message = error.Message }),
                _ => BadRequest(new { error = error.Code, message = error.Message })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Refresh access token
    /// </summary>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RefreshTokenResponse>> RefreshToken([FromBody] RefreshTokenCommand command)
    {
        var commandWithIp = command with { IpAddress = GetClientIpAddress() };
        var result = await _mediator.Send(commandWithIp);

        if (result.IsFailure)
        {
            return Unauthorized(new { error = result.Error!.Code, message = result.Error.Message });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Revoke refresh token (logout)
    /// </summary>
    [HttpPost("revoke-token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RevokeToken([FromBody] RevokeTokenRequest request)
    {
        var command = new RevokeTokenCommand
        {
            RefreshToken = request.RefreshToken,
            IpAddress = GetClientIpAddress()
        };

        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error!.Code, message = result.Error.Message });
        }

        return NoContent();
    }

    /// <summary>
    /// Confirm email address
    /// </summary>
    [HttpGet("confirm-email")]
    [ProducesResponseType(typeof(ConfirmEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConfirmEmailResponse>> ConfirmEmail([FromQuery] string userId, [FromQuery] string token)
    {
        var command = new ConfirmEmailCommand
        {
            UserId = userId,
            Token = token
        };

        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error!.Code, message = result.Error.Message });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Request password reset
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ForgotPasswordResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var command = new ForgotPasswordCommand { Email = request.Email };
        var result = await _mediator.Send(command);

        // Always return success to prevent email enumeration
        return Ok(result.Value);
    }

    /// <summary>
    /// Reset password with token
    /// </summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ResetPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var command = new ResetPasswordCommand
        {
            UserId = request.UserId,
            Token = request.Token,
            NewPassword = request.NewPassword
        };

        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error!.Code, message = result.Error.Message });
        }

        return Ok(result.Value);
    }

    private string? GetClientIpAddress()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',').First().Trim();
        }
        
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}

public record RevokeTokenRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}

public record ForgotPasswordRequest
{
    public string Email { get; init; } = string.Empty;
}

public record ResetPasswordRequest
{
    public string UserId { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}
