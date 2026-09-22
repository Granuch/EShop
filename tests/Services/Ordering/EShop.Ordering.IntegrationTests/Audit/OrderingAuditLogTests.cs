using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.Ordering.Application.Orders.Commands.CancelOrder;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Audit;

/// <summary>
/// Admin panel S15 — Ordering's admin-reachable commands are audited, including the owner-or-admin ones: "who changed
/// this order?" has the same answer shape whether the customer or an operator did it.
/// </summary>
[TestFixture]
[Category("Integration")]
public class OrderingAuditLogTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserId => "audit-admin-1";

    [Test]
    public async Task AnAdminNote_WritesExactlyOneRow_NamingTheOrderAndTheAdmin()
    {
        Guid orderId;
        using (var scope = Factory.Services.CreateScope())
        {
            orderId = (await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, "some-customer")).Id;
        }

        (await Client.PostAsJsonAsync($"/api/v1/orders/{orderId}/notes", new AddOrderNoteRequest { Body = "Called the customer" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var page = await Client.GetFromJsonAsync<AuditLogPageDto>($"/api/v1/admin/audit?entityType=Order&entityId={orderId}");

        var row = page!.Items.Should().ContainSingle().Subject;
        row.Service.Should().Be("ordering");
        row.Action.Should().Be("AddOrderNote");
        row.Outcome.Should().Be("Succeeded");
        row.ActorUserId.Should().Be("audit-admin-1");
    }

    [Test]
    public void AuditRunsOutermost()
    {
        using var scope = Factory.Services.CreateScope();

        var order = scope.ServiceProvider
            .GetServices<IPipelineBehavior<CancelOrderCommand, Result>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ToList();

        order.IndexOf(typeof(AuditBehavior<,>)).Should().Be(0,
            "outside TransactionBehavior, so a commit that fails is recorded as Failed rather than Succeeded");
    }
}

/// <summary>
/// A customer acting on their own order through an owner-or-admin endpoint is audited too, under their own id — and
/// may not read the audit trail.
/// </summary>
[TestFixture]
[Category("Integration")]
public class OrderingAuditLogCustomerTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Customer";
    protected override string TestUserId => "audit-customer-1";

    [Test]
    public async Task AnOwnerCancellingTheirOrder_IsRecordedUnderTheirOwnId()
    {
        Guid orderId;
        using (var scope = Factory.Services.CreateScope())
        {
            orderId = (await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, TestUserId)).Id;
        }

        (await Client.PostAsJsonAsync($"/api/v1/orders/{orderId}/cancel", new CancelOrderRequest { Reason = "changed my mind" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var read = Factory.Services.CreateScope();
        var rows = await read.ServiceProvider.GetRequiredService<IAuditLogReader>().ReadAsync(
            new AuditLogFilter(null, 10, null, null, "Order", orderId.ToString(), null, null, null), CancellationToken.None);

        var row = rows.Items.Should().ContainSingle().Subject;
        row.Action.Should().Be("CancelOrder");
        row.ActorUserId.Should().Be(TestUserId);
    }

    [Test]
    public async Task ACustomer_MayNotReadTheAuditTrail()
        => (await Client.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Test]
    public async Task AnAnonymousCaller_IsUnauthorized()
    {
        using var anonymous = Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
