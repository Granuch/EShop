using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;

namespace EShop.Identity.API.Controllers;

/// <summary>
/// Account management controller - profile, password, 2FA
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccountController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // TODO: Implement GET /api/v1/account/profile
    // [HttpGet("profile")]
    // public async Task<ActionResult> GetProfile()

    // TODO: Implement PUT /api/v1/account/profile
    // [HttpPut("profile")]
    // public async Task<ActionResult> UpdateProfile([FromBody] UpdateProfileCommand command)

    // TODO: Implement POST /api/v1/account/change-password
    // [HttpPost("change-password")]
    // public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordCommand command)

    // TODO: Implement POST /api/v1/account/enable-2fa
    // [HttpPost("enable-2fa")]
    // public async Task<ActionResult> Enable2FA()

    // TODO: Implement POST /api/v1/account/verify-2fa
    // [HttpPost("verify-2fa")]
    // public async Task<ActionResult> Verify2FA([FromBody] Verify2FACommand command)
}
