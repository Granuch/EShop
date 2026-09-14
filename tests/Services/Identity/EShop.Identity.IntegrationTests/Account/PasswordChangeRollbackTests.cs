using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Identity.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Account;

/// <summary>
/// SEC-06, proven rather than approximated.
///
/// <para>
/// Changing a password must revoke every existing session, and the two must be atomic: if the
/// revoke fails, the password must not change either. Otherwise the API reports a successful
/// rotation while every session an attacker already holds stays valid — the precise outcome
/// rotating a password exists to prevent.
/// </para>
///
/// <para>
/// <b>This test could not exist before Stage 8.</b> The existing unit guard
/// (<c>PasswordRotationRevokesSessionsTests</c>) can only assert that the exception is no longer
/// swallowed; it says so in its own summary, because the integration suite ran on EF InMemory
/// where <c>BeginTransaction</c>/<c>Rollback</c> are no-ops and a rollback is unobservable. Now
/// that the suite runs on real PostgreSQL, the rollback itself is assertable — and the assertion
/// that matters is not the status code but that <b>the old password still works afterwards</b>.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class PasswordChangeRollbackTests
{
    private const string ChangePasswordEndpoint = "/api/v1/account/change-password";
    private const string LoginEndpoint = "/api/v1/auth/login";

    private const string OriginalPassword = "Original@123456";
    private const string AttemptedNewPassword = "Attempted@123456";

    private FailingRevokeApiFactory _factory = null!;
    private HttpClient _client = null!;
    private string _email = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _factory = await FailingRevokeApiFactory.CreateAsync();
        _client = _factory.CreateClient();
        await _factory.InitializeDatabaseAsync();

        _email = $"rollback_{Guid.NewGuid():N}@test.com";

        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Email = _email,
            Password = OriginalPassword,
            FirstName = "Roll",
            LastName = "Back"
        });
        register.IsSuccessStatusCode.Should().BeTrue("the fixture needs a real account to rotate");
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    private async Task<string?> LoginAsync(string password)
    {
        var response = await _client.PostAsJsonAsync(LoginEndpoint, new { Email = _email, Password = password });

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("accessToken").GetString();
    }

    [Test]
    public async Task AFailedRevoke_RollsBackThePasswordChangeAndDoesNotReport200()
    {
        var accessToken = await LoginAsync(OriginalPassword);
        accessToken.Should().NotBeNull("the account must be usable before the rotation attempt");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var change = await _client.PostAsJsonAsync(ChangePasswordEndpoint, new
        {
            CurrentPassword = OriginalPassword,
            NewPassword = AttemptedNewPassword
        });

        // The handler no longer swallows the revoke failure, so it propagates and the request
        // fails. What it must never be is 200.
        change.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "reporting success while the revoke failed is exactly the SEC-06 defect");

        _client.DefaultRequestHeaders.Authorization = null;

        // The rollback proof. If the password change had been committed, the old password would be
        // rejected here and the new one accepted — leaving the user with a password change that
        // "failed" but actually took effect, and sessions that were never revoked.
        (await LoginAsync(OriginalPassword)).Should().NotBeNull(
            "the password change must roll back with the failed revoke");
        (await LoginAsync(AttemptedNewPassword)).Should().BeNull(
            "the attempted new password must never have been committed");
    }
}
