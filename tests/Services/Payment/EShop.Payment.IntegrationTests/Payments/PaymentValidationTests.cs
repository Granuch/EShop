using System.Net;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// The request-validation cases for settling a payment (an empty order id, settling twice) moved to
/// <see cref="AdminSettlePaymentTests"/> when that endpoint became admin-only (Payment audit Stage 3).
/// </summary>
[TestFixture]
[Category("Integration")]
public class PaymentValidationTests : AuthenticatedIntegrationTestBase
{
    private const string PaymentsEndpoint = "/api/v1/payments";

    [Test]
    public async Task GetPaymentById_WhenNotFound_ShouldReturnNotFound()
    {
        var response = await Client.GetAsync($"{PaymentsEndpoint}/{Guid.NewGuid()}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
