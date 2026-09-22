using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Fixtures;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Audit;

/// <summary>
/// Admin panel S15. Catalog's admin commands leave one <c>audit_log</c> row each, whatever the outcome, and
/// <c>GET /api/v1/admin/audit</c> serves them. On real Postgres, where a varchar overflow and an aborted transaction
/// are real failures rather than things EF InMemory ignores.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CatalogAuditLogTests : AuthenticatedIntegrationTestBase
{
    private const string AdminId = "audit-admin-0001";

    protected override string TestUserId => AdminId;

    private async Task<Guid> CreateProductAsync(string sku)
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        using var response = await Client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest
        {
            Name = "Audited product",
            Sku = sku,
            Price = 10m,
            StockQuantity = 1,
            CategoryId = categoryId
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    private async Task<List<AuditLogEntry>> RowsForAsync(string entityId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db.Set<AuditLogEntry>().AsNoTracking().Where(e => e.EntityId == entityId).ToListAsync();
    }

    [Test]
    public async Task AnAdminCreate_WritesExactlyOneRow_NamingTheNewProductAndTheActor()
    {
        var sku = CatalogDataHelper.GenerateUniqueSku("AUD");
        var id = await CreateProductAsync(sku);

        var page = await Client.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/v1/admin/audit?entityType=Product&entityId={id}");

        page!.Items.Should().ContainSingle();
        var row = page.Items[0];
        row.Service.Should().Be("catalog");
        row.Action.Should().Be("CreateProduct");
        row.EntityId.Should().Be(id.ToString(), "a create's id comes from its result");
        row.ActorUserId.Should().Be(AdminId);
        row.Outcome.Should().Be("Succeeded");
        row.ErrorCode.Should().BeNull();
        row.PayloadJson.Should().Contain(sku);
    }

    [Test]
    public async Task ARejectedUpdate_IsRecordedAsRejected_WithItsErrorCode()
    {
        var missing = Guid.NewGuid();

        // PUT maps every Result error to 400 (a pre-existing contract), so the status does not say which rejection it was;
        // the audit row's error code does.
        using var response = await Client.PutAsJsonAsync(
            $"/api/v1/products/{missing}", new { ProductId = missing, Price = 12m, StockQuantity = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var row = (await RowsForAsync(missing.ToString())).Should().ContainSingle().Subject;
        row.Action.Should().Be("UpdateProduct");
        row.Outcome.Should().Be(AuditOutcome.Rejected);
        row.ErrorCode.Should().Be("Product.NotFound");
    }

    [Test]
    public async Task AnAdminRead_WritesNothing()
    {
        var id = await CreateProductAsync(CatalogDataHelper.GenerateUniqueSku("AUR"));

        (await Client.GetAsync($"/api/v1/products/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetAsync("/api/v1/products/deleted")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RowsForAsync(id.ToString())).Should().ContainSingle("only the create, not the reads");
    }

    [Test]
    public async Task AnOverlongCorrelationId_IsCut_AndTheRowIsStillWritten()
    {
        // The header is the client's. Uncut, Postgres refuses the insert with 22001 and the audit row is lost.
        //
        // A rejected update, because it writes nothing but the audit row. A create would not do: it raises a domain
        // event, and outbox_messages."CorrelationId" is varchar(100) and NOT cut, so the create itself answers 500 for
        // any correlation id over 100 characters — a pre-existing defect found here and left for its own change.
        var missing = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{missing}")
        {
            Content = JsonContent.Create(new { ProductId = missing, Price = 12m, StockQuantity = 1 })
        };
        request.Headers.Add("X-Correlation-ID", new string('c', 400));

        using var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var row = (await RowsForAsync(missing.ToString())).Should().ContainSingle().Subject;
        row.CorrelationId.Should().HaveLength(AuditLogEntry.CorrelationIdMaxLength);
    }

    [Test]
    public async Task TheEndpoint_PagesNewestFirst_ByBefore()
    {
        for (var i = 0; i < 3; i++)
        {
            await CreateProductAsync(CatalogDataHelper.GenerateUniqueSku("AUP"));
        }

        var first = await Client.GetFromJsonAsync<AuditLogPageDto>($"/api/v1/admin/audit?actorUserId={AdminId}&pageSize=2");
        var second = await Client.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/v1/admin/audit?actorUserId={AdminId}&pageSize=2&before={first!.NextBefore}");

        first.Items.Should().HaveCount(2);
        first.NextBefore.Should().Be(first.Items[^1].Id);
        first.Items.Select(i => i.Id).Should().BeInDescendingOrder();
        second!.Items.Should().NotBeEmpty();
        var lastSeen = first.Items[^1].Id;
        second.Items.Should().OnlyContain(i => i.Id < lastSeen, "the next page starts below the last id seen");
    }

    [Test]
    public async Task AnInvalidQuery_IsA400ValidationFailure()
    {
        using var response = await Client.GetAsync("/api/v1/admin/audit?pageSize=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode.Should().Be("Validation.Failed");
    }

    [Test]
    public async Task ADateWithNoZone_IsAFilter_NotA500()
    {
        using var response = await Client.GetAsync("/api/v1/admin/audit?from=2026-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}

/// <summary>
/// A command whose write reaches the database and is refused there: the SKU pre-check is blinded, so the duplicate hits
/// <c>IX_Products_Sku</c>, Postgres aborts the transaction, and <c>TransactionBehavior</c> rolls back. The audit row
/// must still be written — this is the case "outside the command transaction" (Q8a) exists for.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CatalogAuditLogFailureTests : AuthenticatedIntegrationTestBase
{
    protected override async Task<CatalogApiFactory> CreateFactoryAsync() => await BlindSkuCheckApiFactory.CreateAsync();

    private async Task<HttpResponseMessage> PostAsync(string name, string sku)
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        return await Client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest
        {
            Name = name,
            Sku = sku,
            Price = 10m,
            StockQuantity = 1,
            CategoryId = categoryId
        });
    }

    [Test]
    public async Task ACommandWhoseTransactionIsAborted_IsStillRecorded_AsFailed()
    {
        var sku = CatalogDataHelper.GenerateUniqueSku("AUF");
        (await PostAsync("First", sku)).StatusCode.Should().Be(HttpStatusCode.Created);

        using var response = await PostAsync("Second", sku);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var rows = await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.Action == "CreateProduct" && e.PayloadJson!.Contains(sku))
            .OrderBy(e => e.Id)
            .ToListAsync();

        rows.Select(r => r.Outcome).Should().Equal(AuditOutcome.Succeeded, AuditOutcome.Failed);
        rows[1].ErrorCode.Should().Be("DbUpdateException", "the exception's type, never its message");
        rows[1].EntityId.Should().BeNull("a create that failed has no id to name");
        (await db.Products.CountAsync(p => p.Sku == sku)).Should().Be(1, "and the failed command itself left nothing");
    }
}

/// <summary>The audit endpoint's authorization: <c>audit.read</c>, which a customer does not hold.</summary>
[TestFixture]
[Category("Integration")]
[Category("Security")]
public class CatalogAuditLogAuthorizationTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Customer";

    [Test]
    public async Task ACustomer_IsForbidden()
        => (await Client.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Test]
    public async Task AnAnonymousCaller_IsUnauthorized()
    {
        using var anonymous = Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
