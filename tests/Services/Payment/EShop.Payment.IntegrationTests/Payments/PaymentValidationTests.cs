using System.Net;
using System.Net.Http.Json;

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
}
