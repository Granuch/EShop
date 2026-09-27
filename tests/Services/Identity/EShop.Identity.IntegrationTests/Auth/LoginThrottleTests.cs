using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Auth;

/// <summary>
/// F-28, end to end: after three failed logins the account is throttled for the delay the response
/// advertises, measured from the last failure, and not a moment longer. It used to be refused,
/// right password included, until the failure counter's 15-minute TTL expired, while the body
/// said "Please wait 2 seconds".
/// </summary>
/// <remarks>
/// Uses the real clock through the real pipeline, so it waits about two seconds. The tracker's
/// arithmetic is pinned without waiting in <c>LoginAttemptTrackerTests</c>; this fixture proves
/// the handler and the DI registration (which supplies no <c>TimeProvider</c>, so the system clock
/// is used) put it together as advertised.
/// </remarks>
[TestFixture]
[Category("Integration")]
public class LoginThrottleTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string Password = "Test@123456";

    [Test]
    public async Task AfterThreeFailures_TheRightPasswordWorksOnceTheAdvertisedWaitHasPassed()
    {
        var email = $"throttle-{Guid.NewGuid():N}@test.com";
        await UserManagementHelper.CreateTestUserAsync(Factory.Services, email, Password);

        for (var i = 0; i < 3; i++)
        {
            var failed = await LoginAsync(email, "Wrong@Password1");
            (await ErrorCodeAsync(failed)).Should().Be("Auth.InvalidCredentials", $"failure {i + 1}");
        }

        var throttled = await LoginAsync(email, Password);
        throttled.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await throttled.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("Auth.TooManyAttempts");
        var wait = int.Parse(Regex.Match(problem.Detail!, @"wait (\d+) seconds").Groups[1].Value);
        wait.Should().BeInRange(1, 2, "the delay after three failures is 2 s, counted from the last one");

        await Task.Delay(TimeSpan.FromSeconds(wait) + TimeSpan.FromMilliseconds(300));

        var afterWaiting = await LoginAsync(email, Password);
        afterWaiting.StatusCode.Should().Be(HttpStatusCode.OK,
            "waiting out the advertised delay must let the right password in (F-28)");
        (await afterWaiting.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken
            .Should().NotBeNullOrEmpty();
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password)
        => Client.PostAsJsonAsync(LoginEndpoint, new LoginRequest { Email = email, Password = password });

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())?.ErrorCode;
}
