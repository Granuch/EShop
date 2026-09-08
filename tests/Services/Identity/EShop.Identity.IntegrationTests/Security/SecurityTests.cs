using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Fixtures;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Infrastructure;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Security;

/// <summary>
/// Integration tests for Rate Limiting.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Security")]
public class RateLimitingTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string RegisterEndpoint = "/api/v1/auth/register";

    private const string ClientA = "203.0.113.10";
    private const string ClientB = "203.0.113.11";

    protected override async Task<IdentityApiFactory> CreateFactoryAsync()
        => await RateLimitingApiFactory.CreateAsync();

    [Test]
    public async Task Login_ExceedingRateLimit_ShouldReturn429()
    {
        // Act - Make requests exceeding the test limit (2 per minute)
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            responses.Add(await PostLoginAsync(ClientA));
        }

        // Assert - At least some should be rate limited
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests,
            "because we exceeded the login rate limit for this client");
    }

    [Test]
    public async Task Register_ExceedingRateLimit_ShouldReturn429()
    {
        // Arrange & Act
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            responses.Add(await PostRegisterAsync(ClientA, $"ratelimit{i}@test.com"));
        }

        // Assert - At least some should be rate limited
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests,
            "because we exceeded the auth rate limit for this client");
    }

    /// <summary>
    /// The "login" policy must be partitioned per client. An unpartitioned limiter is one
    /// bucket shared by the whole service, so a single caller could lock every other user out
    /// of logging in.
    /// </summary>
    [Test]
    public async Task Login_ExhaustedByOneClient_ShouldNotRateLimitAnotherClient()
    {
        // Arrange - burn through ClientA's allowance
        HttpResponseMessage? blocked = null;
        for (var i = 0; i < 5; i++)
        {
            blocked = await PostLoginAsync(ClientA);
        }

        blocked!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "because ClientA exhausted its own login bucket");

        // Act - a different client's first request
        var otherClientResponse = await PostLoginAsync(ClientB);

        // Assert
        otherClientResponse.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "because each client IP must get its own login rate limit partition");
    }

    /// <summary>
    /// Same for the controller-wide "auth" policy.
    /// </summary>
    [Test]
    public async Task Auth_ExhaustedByOneClient_ShouldNotRateLimitAnotherClient()
    {
        // Arrange
        HttpResponseMessage? blocked = null;
        for (var i = 0; i < 5; i++)
        {
            blocked = await PostRegisterAsync(ClientA, $"authpartition{i}@test.com");
        }

        blocked!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Act
        var otherClientResponse = await PostRegisterAsync(ClientB, "authpartition-other@test.com");

        // Assert
        otherClientResponse.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "because each client IP must get its own auth rate limit partition");
    }

    /// <summary>
    /// Behind the gateway every request presents the same peer address, so partitioning is
    /// only meaningful if forwarded headers are trusted and applied before the limiter runs.
    /// </summary>
    [Test]
    public async Task Login_BehindTrustedProxy_ShouldPartitionByForwardedClientIp()
    {
        const string forwardedClientA = "198.51.100.10";
        const string forwardedClientB = "198.51.100.11";

        // Arrange - all requests arrive from the gateway; only X-Forwarded-For differs
        HttpResponseMessage? blocked = null;
        for (var i = 0; i < 5; i++)
        {
            blocked = await PostLoginAsync(RateLimitingApiFactory.TrustedProxyIp, forwardedClientA);
        }

        blocked!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "because the forwarded client exhausted its allowance");

        // Act
        var otherClientResponse = await PostLoginAsync(
            RateLimitingApiFactory.TrustedProxyIp, forwardedClientB);

        // Assert - if X-Forwarded-For were ignored both would share the gateway's partition
        otherClientResponse.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "because the limiter must partition on the forwarded client IP, not the gateway IP");
    }

    /// <summary>
    /// X-Forwarded-For from an untrusted peer must be ignored, otherwise a client could mint a
    /// fresh rate limit bucket per request just by varying the header.
    /// </summary>
    [Test]
    public async Task Login_ForwardedForFromUntrustedPeer_ShouldBeIgnored()
    {
        // Arrange & Act - same untrusted peer, a different spoofed client IP every request
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            responses.Add(await PostLoginAsync(ClientA, $"198.51.100.{100 + i}"));
        }

        // Assert
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests,
            "because the peer is not a known proxy, so the spoofed header must not create new partitions");
    }

    private Task<HttpResponseMessage> PostLoginAsync(string remoteIp, string? forwardedFor = null)
    {
        var request = BuildRequest(LoginEndpoint, remoteIp, forwardedFor);
        request.Content = JsonContent.Create(new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = "WrongPassword@123"
        });

        return Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostRegisterAsync(string remoteIp, string email)
    {
        var request = BuildRequest(RegisterEndpoint, remoteIp, forwardedFor: null);
        request.Content = JsonContent.Create(new RegisterRequest
        {
            Email = email,
            Password = "Test@123456",
            FirstName = "Test",
            LastName = "User"
        });

        return Client.SendAsync(request);
    }

    private static HttpRequestMessage BuildRequest(string endpoint, string remoteIp, string? forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add(RateLimitingApiFactory.RemoteIpHeader, remoteIp);

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return request;
    }
}

/// <summary>
/// Integration tests for Security headers and CORS
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Security")]
public class SecurityHeadersTests : IntegrationTestBase
{
    // Response_ShouldNotExposeServerInfo was removed here (TEST-05): TestServer never emits a
    // `Server` header at all, so the assertion passed vacuously regardless of what the real
    // Kestrel-hosted service does. It belongs in a real-Kestrel test, not a WebApplicationFactory
    // one — none exists in this repo today, so this is a deletion, not a rewrite.

    [Test]
    public async Task ApiEndpoints_ShouldReturnJsonContentType()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Test]
    public async Task NonExistentEndpoint_ShouldReturn404()
    {
        // Act
        var response = await Client.GetAsync("/api/v1/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task InvalidHttpMethod_ShouldReturn405()
    {
        // Act - GET on a POST endpoint
        var response = await Client.GetAsync("/api/v1/auth/login");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}

/// <summary>
/// Integration tests for Input Validation and SQL Injection prevention
/// </summary>
[TestFixture]
public class InputValidationTests : IntegrationTestBase
{
    private const string RegisterEndpoint = "/api/v1/auth/register";
    private const string LoginEndpoint = "/api/v1/auth/login";

    [Test]
    public async Task Register_WithSqlInjectionInEmail_ShouldBeSafe()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "test@test.com'; DROP TABLE Users;--",
            Password = "Test@123456",
            FirstName = "Test",
            LastName = "User"
        };

        // Act
        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request);

        // Assert - Should return validation error, not crash
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Login_WithSqlInjectionInEmail_ShouldBeSafe()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "admin@test.com' OR '1'='1",
            Password = "anything"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert - FluentValidation's EmailAddress() rule accepts this string (it only requires
        // an "@" and no leading/trailing whitespace, not a strict RFC 5322 grammar), so the
        // request reaches LoginCommandHandler and is rejected as ordinary invalid credentials.
        // Pinning both outcomes here (as the old BeOneOf(BadRequest, Unauthorized) did) meant
        // the test could never fail; a regression that let the string reach the database
        // undetected would still satisfy it. This asserts the actual current behaviour and
        // that nothing from the attempted injection leaks back to the client.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().NotContain(TestUsers.Admin.Email, "the response must not echo back account data");
        body.Should().NotContain("DROP TABLE").And.NotContain("SELECT");
    }

    [Test]
    public async Task Register_WithXssInName_ShouldBeSafe()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = $"xss_{Guid.NewGuid()}@test.com",
            Password = "Test@123456",
            FirstName = "<script>alert('xss')</script>",
            LastName = "User"
        };

        // Act
        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request);

        // Assert - Should reject or sanitize
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Register_WithVeryLongInput_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = new string('a', 1000) + "@test.com",
            Password = new string('a', 1000),
            FirstName = new string('a', 1000),
            LastName = new string('a', 1000)
        };

        // Act
        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Register_WithNullValues_ShouldReturnBadRequest()
    {
        // Arrange - Send empty JSON
        var request = new { };

        // Act
        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Register_WithNonAsciiName_Succeeds()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = $"unicode_{Guid.NewGuid()}@test.com",
            Password = "Test@123456",
            FirstName = "José",  // Spanish
            LastName = "Müller"  // German
        };

        // Act
        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request);

        // Assert - the name rule is PersonNameRules.Pattern, which admits any Unicode letter.
        // It was `^[a-zA-Z\s'-]+$` until that was fixed, and this test asserted the rejection;
        // the comment then said that if EShop ever intended to accept accented names, this test
        // was what should change. It has. Registration is end to end here, so this also proves
        // the name survives the varchar(50) columns, which count characters rather than bytes.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The other half: widening to Unicode letters must not admit invisible formatting characters.
    /// A right-to-left override reverses how the remainder of a name renders wherever it is shown,
    /// which is a display-spoofing primitive rather than a name.
    /// </summary>
    [Test]
    public async Task Register_WithABidiOverrideInTheName_ShouldReturnBadRequest()
    {
        var request = new RegisterRequest
        {
            Email = $"bidi_{Guid.NewGuid()}@test.com",
            Password = "Test@123456",
            FirstName = "Ab‮cd",
            LastName = "Normal"
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
