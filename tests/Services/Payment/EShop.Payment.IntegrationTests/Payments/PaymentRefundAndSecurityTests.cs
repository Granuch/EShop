using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Payment.IntegrationTests.Payments;

[TestFixture]
[Category("Integration")]
public class PaymentRefundAndSecurityTests : AuthenticatedIntegrationTestBase
{
    private const string PaymentsEndpoint = "/api/v1/payments";

    /// <summary>
    /// Ordering audit Stage 11. Reversed from "ShouldReturnOkAndRefundedStatus": the payment's owner could
    /// refund it at any time, even after the order shipped. Refunds are admin-only now, and the payment
    /// is left untouched. Admin refunds are covered by <see cref="AdminRefundTests"/>.
    /// </summary>
    [Test]
    public async Task RefundPayment_AsThePaymentsOwner_IsForbidden_AndLeavesThePaymentUntouched()
    {
        var created = await CreatePaymentAsync();

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{created.Id}/refund",
            new { Reason = "Customer request" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var current = await Client.GetFromJsonAsync<PaymentResponse>($"{PaymentsEndpoint}/{created.Id}");
        Assert.That(current!.Status, Is.EqualTo("SUCCESS"));
    }

    [Test]
    public async Task CreatePayment_WithoutAuth_ShouldReturnUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.PostAsJsonAsync(PaymentsEndpoint, new
        {
            OrderId = Guid.NewGuid(),
            UserId = TestUserId,
            Amount = 55m,
            Currency = "USD",
            PaymentMethod = "Mock"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task CreatePayment_WhenUserIdDoesNotMatchToken_ShouldReturnForbidden()
    {
        var response = await Client.PostAsJsonAsync(PaymentsEndpoint, new
        {
            OrderId = Guid.NewGuid(),
            UserId = "another-user",
            Amount = 55m,
            Currency = "USD",
            PaymentMethod = "Mock"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetPaymentsByUser_ForAnotherUser_ShouldReturnForbidden()
    {
        var response = await Client.GetAsync("/api/v1/users/another-user/payments");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task CreatePayment_WithTokenWithoutSubjectClaim_ShouldReturnUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateTokenWithoutSubjectClaim());

        var response = await Client.PostAsJsonAsync(PaymentsEndpoint, new
        {
            OrderId = Guid.NewGuid(),
            UserId = TestUserId,
            Amount = 55m,
            Currency = "USD",
            PaymentMethod = "Mock"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>Was 401 for the missing subject claim; the endpoint is now admin-only, so any non-admin is 403.</summary>
    [Test]
    public async Task RefundPayment_WithNonAdminTokenWithoutSubjectClaim_IsForbidden()
    {
        var created = await CreatePaymentAsync();

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateTokenWithoutSubjectClaim());

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{created.Id}/refund",
            new { Reason = "Customer request" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private async Task<PaymentResponse> CreatePaymentAsync()
    {
        var request = new
        {
            OrderId = Guid.NewGuid(),
            UserId = TestUserId,
            Amount = 99.99m,
            Currency = "USD",
            PaymentMethod = "Mock"
        };

        var response = await Client.PostAsJsonAsync(PaymentsEndpoint, request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<PaymentResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return payload!;
    }

    private sealed record PaymentResponse(
        Guid Id,
        Guid OrderId,
        string UserId,
        decimal Amount,
        string Currency,
        string PaymentMethod,
        string Status,
        string? PaymentIntentId,
        string? ErrorMessage,
        DateTime CreatedAt,
        DateTime? ProcessedAt,
        DateTime? UpdatedAt);

    private static string GenerateTokenWithoutSubjectClaim()
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PaymentApiFactory.TestJwtSecretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Email, "user@test.com"),
            new Claim(ClaimTypes.Role, "User"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: PaymentApiFactory.TestJwtIssuer,
            audience: PaymentApiFactory.TestJwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
