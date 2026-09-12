using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EShop.Payment.Domain.Entities;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Payment.IntegrationTests.Payments;

[TestFixture]
[Category("Integration")]
public class PaymentRefundAndSecurityTests : AuthenticatedIntegrationTestBase
{
    private const string PaymentsEndpoint = "/api/v1/payments";

    private Task<PaymentTransaction> SeedOwnSettledPaymentAsync()
        => Factory.SeedPaymentAsync(TestUserId, PaymentStatus.Success, 99.99m, "Mock", "pi_seeded");

    /// <summary>
    /// Ordering audit Stage 11. Reversed from "ShouldReturnOkAndRefundedStatus": the payment's owner could
    /// refund it at any time, even after the order shipped. Refunds are admin-only now, and the payment
    /// is left untouched. Admin refunds are covered by <see cref="AdminRefundTests"/>.
    /// </summary>
    [Test]
    public async Task RefundPayment_AsThePaymentsOwner_IsForbidden_AndLeavesThePaymentUntouched()
    {
        var seeded = await SeedOwnSettledPaymentAsync();

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{seeded.Id}/refund",
            new { Reason = "Customer request" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var current = await Client.GetFromJsonAsync<PaymentResponse>($"{PaymentsEndpoint}/{seeded.Id}");
        Assert.That(current!.Status, Is.EqualTo("SUCCESS"));
    }

    [Test]
    public async Task SettlingAPayment_WithoutAuth_ShouldReturnUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.PostAsJsonAsync(PaymentsEndpoint, new { OrderId = Guid.NewGuid() });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetPaymentsByUser_ForAnotherUser_ShouldReturnForbidden()
    {
        var response = await Client.GetAsync("/api/v1/users/another-user/payments");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>Was 401 for the missing subject claim; settling is admin-only since Payment audit Stage 3, so any non-admin is 403.</summary>
    [Test]
    public async Task SettlingAPayment_WithANonAdminTokenWithoutSubjectClaim_IsForbidden()
    {
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateTokenWithoutSubjectClaim());

        var response = await Client.PostAsJsonAsync(PaymentsEndpoint, new { OrderId = Guid.NewGuid() });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>Was 401 for the missing subject claim; the endpoint is now admin-only, so any non-admin is 403.</summary>
    [Test]
    public async Task RefundPayment_WithNonAdminTokenWithoutSubjectClaim_IsForbidden()
    {
        var seeded = await SeedOwnSettledPaymentAsync();

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateTokenWithoutSubjectClaim());

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{seeded.Id}/refund",
            new { Reason = "Customer request" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
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
