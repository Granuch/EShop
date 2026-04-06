using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EShop.Payment.IntegrationTests.Payments;

[TestFixture]
[Category("Integration")]
public class PaymentValidationTests : AuthenticatedIntegrationTestBase
{
    private const string PaymentsEndpoint = "/api/v1/payments";

    [Test]
    public async Task CreatePayment_WithInvalidAmount_ShouldReturnBadRequest()
    {
        var response = await Client.PostAsJsonAsync(PaymentsEndpoint, new
        {
            OrderId = Guid.NewGuid(),
            UserId = TestUserId,
            Amount = 0m,
            Currency = "USD",
            PaymentMethod = "Mock"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreatePayment_WithDuplicateOrderId_ShouldReturnConflict()
    {
        var orderId = Guid.NewGuid();

        var first = await Client.PostAsJsonAsync(PaymentsEndpoint, new
        {
            OrderId = orderId,
            UserId = TestUserId,
            Amount = 50m,
            Currency = "USD",
            PaymentMethod = "Mock"
        });

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var second = await Client.PostAsJsonAsync(PaymentsEndpoint, new
        {
            OrderId = orderId,
            UserId = TestUserId,
            Amount = 50m,
            Currency = "USD",
            PaymentMethod = "Mock"
        });

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task GetPaymentById_WhenNotFound_ShouldReturnNotFound()
    {
        var response = await Client.GetAsync($"{PaymentsEndpoint}/{Guid.NewGuid()}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RefundPayment_WithInvalidAmount_ShouldReturnBadRequest()
    {
        var created = await CreatePaymentAsync();

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{created.Id}/refund",
            new { Amount = created.Amount + 1m, Reason = "invalid" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RefundPayment_WhenPaymentNotFound_ShouldReturnNotFound()
    {
        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{Guid.NewGuid()}/refund",
            new { Amount = 10m, Reason = "not found" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private async Task<PaymentResponse> CreatePaymentAsync()
    {
        var request = new
        {
            OrderId = Guid.NewGuid(),
            UserId = TestUserId,
            Amount = 100m,
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
