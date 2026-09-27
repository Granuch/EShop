using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.Payment.Application.Payments.Commands.SettleOfflinePayment;
using EShop.Payment.Application.Payments.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Audit;

/// <summary>
/// Admin panel S15 — Payment's admin commands are audited and served on <c>/api/v1/admin/audit</c>. This suite runs on
/// EF InMemory, where the audit writer's own scope still resolves the same named database, so the row is visible to
/// the endpoint.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PaymentAuditLogTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "audit-admin-1";

    [Test]
    public async Task AnOfflineSettlement_WritesExactlyOneRow_NamingThePaymentItCreated()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", amount: 40m);

        var response = await Client.PostAsJsonAsync("/api/v1/payments/offline", new { seeded.OrderId, Reference = "TRF-AUDIT-1" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>();

        var page = await Client.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/v1/admin/audit?entityType=Payment&entityId={payment!.Id}");

        Assert.That(page!.Items, Has.Count.EqualTo(1));
        var row = page.Items[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.Service, Is.EqualTo("payment"));
            Assert.That(row.Action, Is.EqualTo("SettleOfflinePayment"));
            Assert.That(row.Outcome, Is.EqualTo("Succeeded"));
            Assert.That(row.ActorUserId, Is.EqualTo("audit-admin-1"));
            Assert.That(row.PayloadJson, Does.Contain("TRF-AUDIT-1"));
        });
    }

    [Test]
    public async Task ARejectedSettlement_IsRecorded_WithItsErrorCode_AndNoEntityId()
    {
        var orderId = Guid.NewGuid();

        var response = await Client.PostAsJsonAsync("/api/v1/payments/offline", new { OrderId = orderId, Reference = "TRF-AUDIT-2" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var page = await Client.GetFromJsonAsync<AuditLogPageDto>(
            "/api/v1/admin/audit?action=SettleOfflinePayment&outcome=rejected&actorUserId=audit-admin-1");

        var row = page!.Items.Single(i => i.PayloadJson!.Contains(orderId.ToString()));
        Assert.Multiple(() =>
        {
            Assert.That(row.ErrorCode, Is.Not.Null.And.Not.Empty);
            Assert.That(row.EntityId, Is.Null, "a rejected create has no id to name");
        });
    }

    [Test]
    public void AuditRunsOutermost()
    {
        using var scope = Factory.Services.CreateScope();

        var order = scope.ServiceProvider
            .GetServices<IPipelineBehavior<SettleOfflinePaymentCommand, Result<PaymentDto>>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ToList();

        Assert.That(order.IndexOf(typeof(AuditBehavior<,>)), Is.EqualTo(0),
            "outside TransactionBehavior, so a commit that fails is recorded as Failed rather than Succeeded");
    }

    private sealed record PaymentResponse(Guid Id);
}

/// <summary>The audit endpoint requires <c>audit.read</c>, which a customer does not hold.</summary>
[TestFixture]
[Category("Integration")]
public class PaymentAuditLogAuthorizationTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Customer";

    [Test]
    public async Task ACustomer_IsForbidden()
        => Assert.That((await Client.GetAsync("/api/v1/admin/audit")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

    [Test]
    public async Task AnAnonymousCaller_IsUnauthorized()
    {
        using var anonymous = Factory.CreateClient();
        Assert.That((await anonymous.GetAsync("/api/v1/admin/audit")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
