using System.Net;
using System.Net.Http.Json;
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
        => Factory.SeedPaymentAsync(TestUserId, PaymentStatus.Success, 99.99m, "Mock", "pi_seeded");

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

    [Test]
    public async Task GetPaymentsByUser_ShouldContainTheUsersPayment()
    {
        var seeded = await SeedOwnPaymentAsync();

        var response = await Client.GetAsync($"/api/v1/users/{TestUserId}/payments");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var payload = await response.Content.ReadFromJsonAsync<List<PaymentResponse>>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Any(x => x.Id == seeded.Id), Is.True);
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
