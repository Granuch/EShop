using System.Security.Claims;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace EShop.BuildingBlocks.Infrastructure.Authorization;

/// <summary>
/// Soft email verification: signing in does not need a confirmed address, placing an order does. Identity writes
/// <see cref="ClaimType"/> into every access token from <c>ApplicationUser.EmailConfirmed</c> (login and refresh), and the
/// two order entry points — Basket's checkout and Ordering's <c>POST /api/v1/orders</c> — refuse a caller without it.
///
/// <para>
/// <b>Why this is not an authorization policy.</b> A failed policy answers a body-less 403 in every service, and the
/// client must be able to tell "confirm your email" from any other refusal. So the check returns
/// <c>ProblemResults.For(<see cref="NotConfirmed"/>, 403)</c>: the shared envelope, with <c>errorCode</c> and
/// <c>traceId</c>.
/// </para>
///
/// <para>
/// <b>Fail closed.</b> Only a claim that parses as boolean <c>true</c> counts. An absent claim — every token issued
/// before Identity added it — is unverified, which costs such a client one 403 and a refresh rather than letting a
/// token that says nothing place an order. The claim is never mapped by the JWT handler's inbound claim map, so it
/// arrives under this short name.
/// </para>
/// </summary>
public static class EmailVerification
{
    /// <summary>The OpenID Connect standard claim name; its value is <c>true</c> or <c>false</c>.</summary>
    public const string ClaimType = "email_verified";

    /// <summary>
    /// The same code Identity's strict-mode login refusal uses: both mean "confirm the address, then retry", so a
    /// client handles them the same way (offer resend-confirmation).
    /// </summary>
    public static readonly Error NotConfirmed = new(
        "Auth.EmailNotConfirmed",
        "Confirm your email address before placing an order. You can request a new confirmation link.");

    /// <summary>
    /// Whether <paramref name="user"/> carries an <see cref="ClaimType"/> claim of <c>true</c> (any casing) and no other
    /// value for it. Anything else — absent, <c>false</c>, empty, <c>1</c> — is unverified.
    /// </summary>
    public static bool IsVerified(ClaimsPrincipal? user)
    {
        var values = user?.FindAll(ClaimType).Select(claim => claim.Value).ToList() ?? [];

        return values.Count > 0 && values.All(value => bool.TryParse(value, out var verified) && verified);
    }

    /// <summary>
    /// The refusal for a signed-in caller whose address is not verified, or <c>null</c> when the caller may proceed.
    /// An anonymous caller is refused with 401: the filter is meant for endpoints that already require a signed-in user,
    /// and if one were ever mapped without that, letting an anonymous caller through would be the wrong way to fail.
    /// </summary>
    public static IResult? RefusalFor(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return IsVerified(user) ? null : ProblemResults.For(NotConfirmed, StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Refuses the endpoint to a caller without a verified email (<see cref="RefusalFor"/>). It runs after
    /// authorization, so the endpoint's own policy still decides first who may call it at all.
    /// </summary>
    public static RouteHandlerBuilder RequireVerifiedEmail(this RouteHandlerBuilder builder)
        => builder
            .AddEndpointFilter(FilterAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    /// <summary>The endpoint filter behind <see cref="RequireVerifiedEmail"/>; public so it can be tested directly.</summary>
    public static ValueTask<object?> FilterAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var refusal = RefusalFor(context.HttpContext.User);

        return refusal is null ? next(context) : ValueTask.FromResult<object?>(refusal);
    }
}
