using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Reading payments. Since Payment audit Stage 3 no endpoint creates a payment, so each test seeds the one it reads.
/// Settling is covered by <see cref="AdminSettlePaymentTests"/>.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PaymentQueryTests : AuthenticatedIntegrationTestBase
{
    private const string PaymentsEndpoint = "/api/v1/payments";

    private Task<PaymentTransaction> SeedOwnPaymentAsync()
        => Factory.SeedPaymentAsync(TestUserId, PaymentStatus.Success, 99.99m, PaymentMethodType.Mock, "pi_seeded");

    [Test]
    public async Task GetPaymentById_ForTheOwner_ShouldReturnPayment()
    {
        var seeded = await SeedOwnPaymentAsync();

        var response = await Client.GetAsync($"{PaymentsEndpoint}/{seeded.Id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var payload = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Id, Is.EqualTo(seeded.Id));
        Assert.That(payload.UserId, Is.EqualTo(TestUserId));
        Assert.That(payload.Status, Is.EqualTo("SUCCESS"));
    }

    /// <summary>
    /// Payment audit Stage 12. Another customer's payment answers exactly what a missing id does. It used to be 403,
    /// which confirmed that the id existed.
    /// </summary>
    [Test]
    public async Task GetPaymentById_ForAnotherCustomersPayment_IsIndistinguishableFromAMissingOne()
    {
        var theirs = await Factory.SeedPaymentAsync("another-customer", PaymentStatus.Success, 10m, PaymentMethodType.Mock, "pi_theirs");

        var forTheirs = await Client.GetAsync($"{PaymentsEndpoint}/{theirs.Id}");
        var forMissing = await Client.GetAsync($"{PaymentsEndpoint}/{Guid.NewGuid()}");

        using var theirsBody = JsonDocument.Parse(await forTheirs.Content.ReadAsStringAsync());
        using var missingBody = JsonDocument.Parse(await forMissing.Content.ReadAsStringAsync());
        Assert.Multiple(() =>
        {
            Assert.That(forTheirs.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(forMissing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(theirsBody.RootElement.GetProperty("errorCode").GetString(),
                Is.EqualTo(missingBody.RootElement.GetProperty("errorCode").GetString()));
            Assert.That(theirsBody.RootElement.GetProperty("detail").GetString(),
                Is.EqualTo(missingBody.RootElement.GetProperty("detail").GetString()));
        });
    }

    [Test]
    public async Task GetPaymentsByUser_ShouldContainTheUsersPayment()
    {
        var seeded = await SeedOwnPaymentAsync();

        var response = await Client.GetAsync($"/api/v1/users/{TestUserId}/payments");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Payment audit Stage 10 (D10): one page, not a bare array. Paging itself is in PaymentListTests.
        var payload = await response.Content.ReadFromJsonAsync<PaymentPage>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Items.Any(x => x.Id == seeded.Id), Is.True);
    }

    private sealed record PaymentPage(List<PaymentResponse> Items, int TotalCount);

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
