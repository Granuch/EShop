using System.Security.Claims;
using System.Text.Json;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.UnitTests.Authorization;

/// <summary>
/// Soft email verification, Stage 1. <see cref="EmailVerification"/> is the one place that decides whether a token's
/// <c>email_verified</c> claim lets its holder place an order, for both Basket's checkout and Ordering's
/// <c>POST /orders</c>. It must fail closed: only a literal boolean <c>true</c> counts.
/// </summary>
[TestFixture]
public class EmailVerificationTests
{
    private static ClaimsPrincipal SignedIn(params Claim[] claims)
        => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-1"), .. claims], authenticationType: "TestAuth"));

    private static Claim Verified(string value) => new(EmailVerification.ClaimType, value);

    [Test]
    public void TheClaimName_IsTheOpenIdConnectStandardOne()
    {
        Assert.That(EmailVerification.ClaimType, Is.EqualTo("email_verified"));
    }

    [TestCase("true")]
    [TestCase("True")]
    [TestCase("TRUE")]
    public void ABooleanTrue_IsVerified(string value)
    {
        Assert.That(EmailVerification.IsVerified(SignedIn(Verified(value))), Is.True);
    }

    [TestCase("false")]
    [TestCase("False")]
    [TestCase("")]
    [TestCase("1")]
    [TestCase("yes")]
    public void AnythingElse_IsNotVerified(string value)
    {
        Assert.That(EmailVerification.IsVerified(SignedIn(Verified(value))), Is.False);
    }

    [Test]
    public void AnAbsentClaim_IsNotVerified()
    {
        // Every token issued before Identity added the claim looks like this; it must not place an order.
        Assert.That(EmailVerification.IsVerified(SignedIn()), Is.False);
    }

    [Test]
    public void AConflictingPair_IsNotVerified()
    {
        Assert.That(EmailVerification.IsVerified(SignedIn(Verified("true"), Verified("false"))), Is.False);
    }

    [Test]
    public void ANullPrincipal_IsNotVerified()
    {
        Assert.That(EmailVerification.IsVerified(null), Is.False);
    }

    [Test]
    public void TheError_IsAuthEmailNotConfirmed()
    {
        Assert.That(EmailVerification.NotConfirmed.Code, Is.EqualTo("Auth.EmailNotConfirmed"));
    }

    [Test]
    public async Task AVerifiedCaller_ReachesTheEndpoint()
    {
        var (result, reached, _) = await RunFilterAsync(SignedIn(Verified("true")));

        Assert.That(reached, Is.True);
        Assert.That(result, Is.EqualTo("endpoint ran"));
    }

    [Test]
    public async Task AnUnverifiedCaller_Gets403AuthEmailNotConfirmed_InTheSharedEnvelope()
    {
        var (_, reached, context) = await RunFilterAsync(SignedIn(Verified("false")));

        Assert.That(reached, Is.False, "the endpoint must not run");
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That(context.Response.ContentType, Does.StartWith("application/problem+json"));

        var body = await ReadBodyAsync(context);
        Assert.That(body.GetProperty("status").GetInt32(), Is.EqualTo(403));
        Assert.That(body.GetProperty("errorCode").GetString(), Is.EqualTo("Auth.EmailNotConfirmed"));
        Assert.That(body.GetProperty("detail").GetString(), Is.EqualTo(EmailVerification.NotConfirmed.Message));
        Assert.That(body.GetProperty("traceId").GetString(), Is.EqualTo("trace-email"));
    }

    [Test]
    public async Task ACallerWithNoClaim_IsRefusedTheSameWay()
    {
        var (_, reached, context) = await RunFilterAsync(SignedIn());

        Assert.That(reached, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That((await ReadBodyAsync(context)).GetProperty("errorCode").GetString(),
            Is.EqualTo("Auth.EmailNotConfirmed"));
    }

    [Test]
    public async Task AnAnonymousCaller_IsRefusedWith401_NeverLetThrough()
    {
        // Even a verified-looking claim on an unauthenticated identity proves nothing.
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([Verified("true")]));

        var (_, reached, context) = await RunFilterAsync(anonymous);

        Assert.That(reached, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
    }

    /// <summary>Runs the filter, executing whatever <see cref="IResult"/> it short-circuits with.</summary>
    private static async Task<(object? Result, bool Reached, HttpContext Context)> RunFilterAsync(ClaimsPrincipal user)
    {
        var context = new DefaultHttpContext
        {
            // Results.Problem resolves services at execute time; Response.Body defaults to Stream.Null.
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
            TraceIdentifier = "trace-email",
            User = user
        };

        var reached = false;
        var result = await EmailVerification.FilterAsync(
            new DefaultEndpointFilterInvocationContext(context),
            _ =>
            {
                reached = true;
                return ValueTask.FromResult<object?>("endpoint ran");
            });

        if (result is IResult refusal)
        {
            await refusal.ExecuteAsync(context);
        }

        return (result, reached, context);
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }
}
