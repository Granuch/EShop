using System.Net;
using System.Net.Http.Json;
using EShop.Payment.Domain.Entities;

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
        var created = await SeedCustomerPaymentAsync();

        var response = await Client.PostAsJsonAsync($"{PaymentsEndpoint}/{created.Id}/refund", new { Reason = "Damaged" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var payload = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.That(payload!.Status, Is.EqualTo("REFUNDED"));
    }

    [Test]
    public async Task AnAdmin_NamingTheFullAmount_IsAccepted()
    {
        var created = await SeedCustomerPaymentAsync();

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{created.Id}/refund",
            new { Amount = created.Amount, Reason = "Damaged" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>A partial refund used to mark the whole payment Refunded. It is refused and changes nothing.</summary>
    [Test]
    public async Task APartialRefund_IsRefused_AndLeavesThePaymentUntouched()
    {
        var created = await SeedCustomerPaymentAsync();

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
        var created = await SeedCustomerPaymentAsync();

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

    /// <summary>
    /// Payment audit Stage 12 (D15). The admin's reason is recorded as the refunded payment's note. It used to be
    /// accepted and discarded.
    /// </summary>
    [Test]
    public async Task TheAdminsReason_IsRecordedOnTheRefundedPayment()
    {
        var created = await SeedCustomerPaymentAsync();

        var response = await Client.PostAsJsonAsync(
            $"{PaymentsEndpoint}/{created.Id}/refund",
            new { Reason = "  Damaged in transit  " });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var refunded = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        var current = await Client.GetFromJsonAsync<PaymentResponse>($"{PaymentsEndpoint}/{created.Id}");
        Assert.Multiple(() =>
        {
            Assert.That(refunded!.ErrorMessage, Is.EqualTo("Damaged in transit"));
            Assert.That(current!.ErrorMessage, Is.EqualTo("Damaged in transit"));
        });
    }

    /// <summary>
    /// Payment audit Stage 12 (D16). Each refusal names its cause. All of them used to be PAYMENT_ALREADY_PROCESSED, a
    /// Pending payment included.
    /// </summary>
    [TestCase(PaymentStatus.Refunded, "PAYMENT_ALREADY_REFUNDED")]
    [TestCase(PaymentStatus.Pending, "PAYMENT_NOT_CAPTURED")]
    [TestCase(PaymentStatus.Processing, "PAYMENT_NOT_CAPTURED")]
    [TestCase(PaymentStatus.Failed, "PAYMENT_NOT_CAPTURED")]
    [TestCase(PaymentStatus.Cancelled, "PAYMENT_NOT_CAPTURED")]
    public async Task APaymentThatCannotBeRefunded_IsAConflict_ThatSaysWhy(PaymentStatus status, string errorCode)
    {
        var payment = await Factory.SeedPaymentAsync("customer-1", status, 99.99m, PaymentMethodType.Mock, "pi_seeded");

        var response = await Client.PostAsJsonAsync($"{PaymentsEndpoint}/{payment.Id}/refund", new { Reason = "x" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(errorCode));
    }

    /// <summary>A customer's simulated payment that succeeded. Seeded: no endpoint creates payments since Payment audit Stage 3.</summary>
    private Task<PaymentTransaction> SeedCustomerPaymentAsync()
        => Factory.SeedPaymentAsync("customer-1", PaymentStatus.Success, 99.99m, PaymentMethodType.Mock, "pi_seeded");

    private sealed record PaymentResponse(Guid Id, decimal Amount, string Status, string? ErrorMessage);
}
