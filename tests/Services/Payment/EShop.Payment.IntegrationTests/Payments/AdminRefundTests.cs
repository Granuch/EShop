using System.Net;
using System.Net.Http.Json;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Ordering audit Stage 11: refunds are admin-only and full-only. The owner's 403 is in
/// <see cref="PaymentRefundAndSecurityTests"/>.
/// </summary>
[TestFixture]
[Category("Integration")]
public class AdminRefundTests : AuthenticatedIntegrationTestBase
{
    private const string PaymentsEndpoint = "/api/v1/payments";

    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-1";

    [Test]
    public async Task AnAdmin_RefundsACustomersPayment_InFull()
    {
        var created = await CreateCustomerPaymentAsync();

        var response = await Client.PostAsJsonAsync($"{PaymentsEndpoint}/{created.Id}/refund", new { Reason = "Damaged" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var payload = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.That(payload!.Status, Is.EqualTo("REFUNDED"));
    }

    [Test]
    public async Task AnAdmin_NamingTheFullAmount_IsAccepted()
    {
        var created = await CreateCustomerPaymentAsync();

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{created.Id}/refund",
            new { Amount = created.Amount, Reason = "Damaged" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>A partial refund used to mark the whole payment Refunded. It is refused and changes nothing.</summary>
    [Test]
    public async Task APartialRefund_IsRefused_AndLeavesThePaymentUntouched()
    {
        var created = await CreateCustomerPaymentAsync();

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{created.Id}/refund",
            new { Amount = 10m, Reason = "One item damaged" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("PARTIAL_REFUND_NOT_SUPPORTED"));

        var current = await Client.GetFromJsonAsync<PaymentResponse>($"{PaymentsEndpoint}/{created.Id}");
        Assert.That(current!.Status, Is.EqualTo("SUCCESS"));
    }

    /// <summary>Moved from PaymentValidationTests, which signs in as a customer and now gets 403.</summary>
    [Test]
    public async Task AnAmountAboveTheTotal_IsRefused()
    {
        var created = await CreateCustomerPaymentAsync();

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{created.Id}/refund",
            new { Amount = created.Amount + 1m, Reason = "too much" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("PARTIAL_REFUND_NOT_SUPPORTED"));
    }

    /// <summary>Moved from PaymentValidationTests, which signs in as a customer and now gets 403.</summary>
    [Test]
    public async Task RefundingAMissingPayment_IsNotFound()
    {
        var response = await Client.PostAsJsonAsync($"{PaymentsEndpoint}/{Guid.NewGuid()}/refund", new { Reason = "x" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private async Task<PaymentResponse> CreateCustomerPaymentAsync()
    {
        var response = await Client.PostAsJsonAsync(PaymentsEndpoint, new
        {
            OrderId = Guid.NewGuid(),
            UserId = "customer-1",
            Amount = 99.99m,
            Currency = "USD",
            PaymentMethod = "Mock"
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
    }

    private sealed record PaymentResponse(Guid Id, decimal Amount, string Status);
}
