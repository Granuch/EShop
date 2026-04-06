using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EShop.Payment.IntegrationTests.Payments;

[TestFixture]
[Category("Integration")]
public class PaymentQueryTests : AuthenticatedIntegrationTestBase
{
    private const string PaymentsEndpoint = "/api/v1/payments";

    [Test]
    public async Task CreatePayment_WithValidRequest_ShouldReturnCreated()
    {
        var request = new
        {
            OrderId = Guid.NewGuid(),
            UserId = TestUserId,
            Amount = 120.5m,
            Currency = "USD",
            PaymentMethod = "Mock"
        };

        var response = await Client.PostAsJsonAsync(PaymentsEndpoint, request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.OrderId, Is.EqualTo(request.OrderId));
        Assert.That(created.UserId, Is.EqualTo(TestUserId));
        Assert.That(created.Status, Is.EqualTo("SUCCESS"));
    }

    [Test]
    public async Task GetPaymentById_AfterCreate_ShouldReturnPayment()
    {
        var createdPayment = await CreatePaymentAsync();

        var response = await Client.GetAsync($"{PaymentsEndpoint}/{createdPayment.Id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var payload = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Id, Is.EqualTo(createdPayment.Id));
        Assert.That(payload.UserId, Is.EqualTo(TestUserId));
    }

    [Test]
    public async Task GetPaymentsByUser_AfterCreate_ShouldContainCreatedPayment()
    {
        var createdPayment = await CreatePaymentAsync();

        var response = await Client.GetAsync($"/api/v1/users/{TestUserId}/payments");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var payload = await response.Content.ReadFromJsonAsync<List<PaymentResponse>>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Any(x => x.Id == createdPayment.Id), Is.True);
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
