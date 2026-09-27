using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Mvc;

namespace EShop.Identity.API.Controllers;

/// <summary>
/// Base for Identity's MVC controllers. Identity is the only service in the repo using
/// controllers rather than minimal APIs, and its Result-failure path used to return an anonymous
/// <c>{ error, message }</c> object while its exception path returned problem+json — so one
/// endpoint answered in two shapes. These helpers put the Result path on the same canonical
/// RFC 7807 envelope every other service emits.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// The caller always supplies the status. It is deliberately NOT derived from the error code
    /// the way Catalog's ProblemForError derives 404 from a ".NotFound" suffix: Identity's
    /// Auth.InvalidCredentials must stay 401, and several actions discriminate on a
    /// <see cref="FieldValidationError"/> to choose between 400 and 401.
    ///
    /// <para>Pass the <see cref="Error"/> itself, never its Code and Message: only this overload
    /// writes a validation failure's <c>errors</c> map (frontend-contracts F-03).</para>
    ///
    /// <para>Named ProblemForError rather than Problem because ControllerBase already declares
    /// <c>Problem(string?, string?, int?, string?, string?)</c>. An overload named Problem would
    /// win resolution today but silently fall through to the base method — returning a different
    /// body — the first time someone made the status parameter nullable.</para>
    /// </summary>
    protected ActionResult ProblemForError(Error error, int statusCode)
        => new ObjectResult(EShopProblem.ForError(HttpContext, error, statusCode))
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };

    /// <summary>
    /// For failures that never came from a <see cref="Result"/> and so have no
    /// <see cref="Error"/> to unwrap — chiefly the pre-dispatch claims checks in
    /// <c>AccountController</c>, <c>AuthController</c> and <c>UsersController</c>, which reject
    /// a request before any command is built (e.g. a token with no subject claim).
    ///
    /// <para><c>RolesController</c> used to be the heaviest consumer: with no CQRS layer it
    /// hand-wrote all thirteen of its failure sites here. Stage 7 moved it behind MediatR, so it
    /// now uses the <see cref="Error"/> overload exclusively. Prefer that overload — reach for
    /// this one only where there genuinely is no Result, or the error codes start drifting the
    /// way Roles' did.</para>
    /// </summary>
    protected ActionResult ProblemForError(string errorCode, string message, int statusCode)
        => new ObjectResult(EShopProblem.Create(HttpContext, statusCode, detail: message, errorCode: errorCode))
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
}
