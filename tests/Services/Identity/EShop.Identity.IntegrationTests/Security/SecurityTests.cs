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
[Explicit("In-memory TestServer does not currently enforce endpoint-specific ASP.NET rate limiter metadata deterministically.")]
public class RateLimitingTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string RegisterEndpoint = "/api/v1/auth/register";

    protected override IdentityApiFactory CreateFactory()
    {
        return new RateLimitingApiFactory();
    }

    [Test]
    public async Task Login_ExceedingRateLimit_ShouldReturn429()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = "WrongPassword@123"
        };

        // Act - Make requests exceeding the test limit (2 per minute)
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            responses.Add(await Client.PostAsJsonAsync(LoginEndpoint, loginRequest));
        }

        // Assert - At least some should be rate limited
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests,
            "because we exceeded the rate limit of 5 requests");
    }

    [Test]
    public async Task Register_ExceedingRateLimit_ShouldReturn429()
    {
        // Arrange & Act
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            var request = new RegisterRequest
            {
                Email = $"ratelimit{i}@test.com",
                Password = "Test@123456",
                FirstName = "Test",
                LastName = "User"
            };

            responses.Add(await Client.PostAsJsonAsync(RegisterEndpoint, request));
        }

        // Assert - At least some should be rate limited
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests,
            "because we exceeded the rate limit");
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
    [Test]
    public async Task Response_ShouldNotExposeServerInfo()
    {
        // Act
        var response = await Client.GetAsync("/health");

        // Assert - Should not expose server version info
        response.Headers.Should().NotContain(h => h.Key.Equals("Server", StringComparison.OrdinalIgnoreCase) && h.Value.Any(v => v.Contains("Kestrel")));
    }

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

        // Assert - Should return validation error or unauthorized, not data leak
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
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
    public async Task Register_WithSpecialUnicodeCharacters_ShouldHandleGracefully()
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

        // Assert - Should either accept or reject gracefully (depends on validation rules)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }
}
