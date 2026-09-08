using EShop.Identity.Application.Users.Queries.GetUserContact;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EShop.Identity.API.Controllers;

[ApiController]
[Route("api/v1/users")]
// API-8. The class-level default is the InternalService policy, not a bare [Authorize].
//
// Multiple [Authorize] attributes are **combined**, not overridden: a bare [Authorize] here plus
// [Authorize(Policy = "InternalService")] on an action requires BOTH an authenticated JWT and the
// API key, which breaks every caller — Notification presents an API key and no token. Declaring
// the policy at class level instead gives the same "secure by default for new actions" property
// without changing what this controller requires.
[Authorize(Policy = "InternalService")]
public class UsersController : ApiControllerBase
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
    public async Task<ActionResult<UserContactResponse>> GetContact(string userId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUserContactQuery { UserId = userId }, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error?.Code == "Validation.Failed")
            {
                return ProblemForError(result.Error.Code, result.Error.Message, StatusCodes.Status400BadRequest);
            }

            return ProblemForError(result.Error!.Code, result.Error.Message, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }
}
