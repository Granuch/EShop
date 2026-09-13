using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace EShop.Payment.IntegrationTests.Errors;

/// <summary>
/// Payment audit Stage 10 (M6). Only a real conflict is a 409: a lost row-version race, or a unique index. Payment used to
/// answer 409 for every <see cref="DbUpdateException"/>. So a value too long for its column, or a missing required value,
/// told the client to retry a request that could never succeed.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PersistenceFailureMappingTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-1";

    private async Task<(HttpStatusCode Status, string? ErrorCode)> SettleEndingWithAsync(Exception failure)
    {
        using var factory = new FailingSettlementPaymentApiFactory(failure);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        var payment = await factory.SeedPaymentAsync("customer-1");

        var response = await client.PostAsJsonAsync("/api/v1/payments", new { payment.OrderId });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode,
            body.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null);
    }

    [Test]
    public async Task APersistenceFailure_ThatIsNotAConflict_IsAServerError()
    {
        var (status, errorCode) = await SettleEndingWithAsync(new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException("22001: value too long for type character varying(100)")));

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(errorCode, Is.EqualTo(ProblemErrorCodes.InternalServerError));
        });
    }

    [Test]
    public async Task ALostRowVersionRace_IsAConflict()
    {
        var (status, errorCode) = await SettleEndingWithAsync(new DbUpdateConcurrencyException(
            "The database operation was expected to affect 1 row(s), but actually affected 0 row(s)."));

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(errorCode, Is.EqualTo(ProblemErrorCodes.ConcurrencyConflict));
        });
    }

    [Test]
    public async Task AUniqueIndexViolation_IsAConflict()
    {
        var (status, errorCode) = await SettleEndingWithAsync(new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException("23505: duplicate key value violates unique constraint \"IX_PaymentTransactions_OrderId\"")));

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(errorCode, Is.EqualTo(ProblemErrorCodes.DuplicateResource));
        });
    }
}
