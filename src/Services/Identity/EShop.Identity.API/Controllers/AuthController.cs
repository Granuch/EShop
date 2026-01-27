using Microsoft.AspNetCore.Mvc;
using MediatR;
using EShop.Identity.Application.Auth.Commands.Register;
using EShop.Identity.Application.Auth.Commands.Login;
using EShop.Identity.Application.Auth.Commands.RefreshToken;

namespace EShop.Identity.API.Controllers;

/// <summary>
/// Authentication controller - handles login, register, token refresh
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // TODO: Implement POST /api/v1/auth/register
    // [HttpPost("register")]
    // public async Task<ActionResult> Register([FromBody] RegisterCommand command)

    // TODO: Implement POST /api/v1/auth/login
    // [HttpPost("login")]
    // public async Task<ActionResult> Login([FromBody] LoginCommand command)

    // TODO: Implement POST /api/v1/auth/refresh-token
    // [HttpPost("refresh-token")]
    // public async Task<ActionResult> RefreshToken([FromBody] RefreshTokenCommand command)

    // TODO: Implement POST /api/v1/auth/revoke-token (logout)
    // [HttpPost("revoke-token")]
    // public async Task<ActionResult> RevokeToken([FromBody] RevokeTokenCommand command)

    // TODO: Add rate limiting
    // TODO: Add request logging with correlation ID
}
