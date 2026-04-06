using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EShop.Payment.IntegrationTests.Payments;

[TestFixture]
[Category("Integration")]
public class PaymentRefundAndSecurityTests : AuthenticatedIntegrationTestBase
{
    private const string PaymentsEndpoint = "/api/v1/payments";

    [Test]
    public async Task RefundPayment_WhenPaymentSucceeded_ShouldReturnOkAndRefundedStatus()
    {
        var created = await CreatePaymentAsync();

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{created.Id}/refund",
            new { Amount = 10m, Reason = "Customer request" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var payload = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Status, Is.EqualTo("REFUNDED"));
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
}
