using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Identity.IntegrationTests.Fixtures;
using EShop.Identity.IntegrationTests.Infrastructure;
using EShop.Identity.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Users;

/// <summary>
/// TEST-06. The <c>InternalService</c> API-key policy had <b>no coverage at all</b> before Stage 8,
/// and it is the only thing standing between the internet and
/// <c>GET /api/v1/users/{userId}/contact</c> — an endpoint that returns a user's email address and
/// takes no JWT. Notification calls it service-to-service.
///
/// <para>
/// The gap was self-perpetuating: the tracked config ships an empty <c>ApiKey</c> and Testing does
/// not override it, so the handler denies everything under test unless a key is supplied
/// deliberately (see <see cref="InternalServiceApiFactory"/>). A policy that rejects everything
/// looks identical to a policy that works, from the outside.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class InternalServicePolicyTests
{
    private InternalServiceApiFactory _factory = null!;
    private HttpClient _client = null!;
    private string _userId = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _factory = await InternalServiceApiFactory.CreateAsync();
        _client = _factory.CreateClient();
        await _factory.InitializeDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        _userId = await db.Users.Where(u => u.Email == "user@test.com").Select(u => u.Id).SingleAsync();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    private HttpRequestMessage ContactRequest(string userId, string? apiKey, string? headerName = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{userId}/contact");
        if (apiKey is not null)
        {
            request.Headers.Add(headerName ?? InternalServiceApiFactory.HeaderName, apiKey);
        }

        return request;
    }

    [Test]
    public async Task WithTheCorrectApiKey_ReturnsTheContact()
    {
        var response = await _client.SendAsync(
            ContactRequest(_userId, InternalServiceApiFactory.ConfiguredApiKey));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("email").GetString().Should().Be("user@test.com");
    }

    /// <summary>
    /// No key at all. The policy carries only <c>InternalServiceRequirement</c> — no
    /// <c>RequireAuthenticatedUser</c> — so the caller is anonymous when it fails, and the
    /// authorization middleware challenges rather than forbids. Pinning the actual status matters
    /// because 401-vs-403 here is not the usual "authenticated but not allowed" distinction.
    /// </summary>
    [Test]
    public async Task WithNoApiKey_IsRejected()
    {
        var response = await _client.SendAsync(ContactRequest(_userId, apiKey: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task WithAWrongApiKey_IsRejected()
    {
        var response = await _client.SendAsync(ContactRequest(_userId, "not-the-configured-key"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A key of the right value under the wrong header name must not authorize. The header name is
    /// configurable, so this guards against the handler ever falling back to a hardcoded one.
    /// </summary>
    [Test]
    public async Task WithTheCorrectKeyUnderTheWrongHeader_IsRejected()
    {
        var response = await _client.SendAsync(
            ContactRequest(_userId, InternalServiceApiFactory.ConfiguredApiKey, headerName: "X-Some-Other-Header"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A prefix of the real key must fail. <c>FixedTimeEquals</c> returns false immediately when
    /// lengths differ rather than comparing in constant time — that is API-6's timing leak, still
    /// open — but it must at least still return false.
    /// </summary>
    [Test]
    public async Task WithAPrefixOfTheCorrectKey_IsRejected()
    {
        var prefix = InternalServiceApiFactory.ConfiguredApiKey[..10];

        var response = await _client.SendAsync(ContactRequest(_userId, prefix));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task WithAValidKeyButUnknownUser_Returns404()
    {
        var response = await _client.SendAsync(
            ContactRequest(Guid.NewGuid().ToString(), InternalServiceApiFactory.ConfiguredApiKey));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A JWT — even an admin's — must not open this endpoint. The policy is API-key-only by design;
    /// if it ever gained an authenticated-user fallback, every logged-in user could read every
    /// other user's email address.
    /// </summary>
    [Test]
    public async Task AnAdminJwtDoesNotSubstituteForTheApiKey()
    {
        var login = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Email = "admin@test.com", Password = "Admin@123456" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = JsonDocument.Parse(await login.Content.ReadAsStringAsync())
            .RootElement.GetProperty("accessToken").GetString();

        var request = ContactRequest(_userId, apiKey: null);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an authenticated caller that fails the policy is forbidden, not challenged");
    }
}

/// <summary>
/// TEST-06 / SEC-08. The unconfigured case, which needs its own host.
///
/// If <c>InternalServiceAuth:ApiKey</c> is blank the handler returns without calling
/// <c>Succeed</c>, so the policy denies. That is fail-closed and it is the behaviour worth pinning:
/// the opposite — treating "no key configured" as "no key required" — would silently expose the
/// endpoint on any deployment that forgot to set the secret, which is exactly the shape of the
/// placeholder-secret problem SEC-08 dealt with elsewhere.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class InternalServicePolicyUnconfiguredTests
{
    private InternalServiceApiFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _factory = await InternalServiceApiFactory.CreateAsync(apiKey: string.Empty);
        _client = _factory.CreateClient();
        await _factory.InitializeDatabaseAsync();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [Test]
    public async Task WithNoKeyConfigured_TheEndpointIsClosedRatherThanOpen()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/users/{Guid.NewGuid()}/contact");
        request.Headers.Add(InternalServiceApiFactory.HeaderName, "anything-at-all");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a blank configured key must deny every caller, never admit them");
    }
}
