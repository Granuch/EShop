using EShop.Identity.Application.Users.Queries.GetUserContact;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EShop.Identity.API.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{userId}/contact")]
    [Authorize(Policy = "InternalService")]
    [ProducesResponseType(typeof(UserContactResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserContactResponse>> GetContact(string userId)
    {
        var result = await _mediator.Send(new GetUserContactQuery { UserId = userId });

        if (result.IsFailure)
        {
            if (result.Error?.Code == "Validation.Failed")
            {
                return BadRequest(new { error = result.Error.Code, message = result.Error.Message });
            }

            return NotFound(new { error = result.Error!.Code, message = result.Error.Message });
        }

        return Ok(result.Value);
    }
}
