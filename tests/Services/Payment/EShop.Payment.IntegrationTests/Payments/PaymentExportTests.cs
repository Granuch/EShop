using System.Net;
using EShop.Payment.Application.Payments.Queries.ExportPayments;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Admin panel S10 (endpoint #69). <c>GET /api/v1/payments/export</c> — the accounting CSV, over the same filters as
/// the admin list. The customer's 403 is in <see cref="Security.NonAdminAuthorizationTests"/>.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PaymentExportTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-1";

    private static readonly DateTime Base = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private async Task<PaymentTransaction> SeedAsync(
        PaymentStatus status = PaymentStatus.Success,
        decimal amount = 100m,
        string? errorMessage = null,
        string userId = "customer-1")
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = userId,
            Amount = amount,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = $"pi_{Guid.NewGuid():N}",
            Status = status,
            ErrorMessage = errorMessage,
            CreatedAt = Base,
            UpdatedAt = Base
        };
        await Factory.SeedAsync(payment);
        return payment;
    }

    private async Task<(HttpResponseMessage Response, string Body)> ExportAsync(string query = "")
    {
        var response = await Client.GetAsync($"/api/v1/payments/export{query}");
        return (response, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task TheExport_IsCsv_WithAHeaderAndOneRowPerPayment()
    {
        await SeedAsync();
        await SeedAsync();

        var (response, body) = await ExportAsync();

        var lines = body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("text/csv"));
            Assert.That(lines[0].TrimStart('﻿'), Is.EqualTo(string.Join(',', PaymentCsvColumns)));
            Assert.That(lines, Has.Length.EqualTo(3));
        });
    }

    /// <summary>An export is a file, not a page of text; the disposition is what makes a browser save it.</summary>
    [Test]
    public async Task TheExport_IsOfferedAsADownload()
    {
        await SeedAsync();

        var (response, _) = await ExportAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.Content.Headers.ContentDisposition!.DispositionType, Is.EqualTo("attachment"));
            Assert.That(response.Content.Headers.ContentDisposition!.FileName, Does.Contain("payments-"));
            Assert.That(response.Content.Headers.ContentDisposition!.FileName, Does.Contain(".csv"));
        });
    }

    /// <summary>Excel reads a BOM-less file as the machine's ANSI code page, so a non-ASCII note arrives mangled.</summary>
    [Test]
    public async Task TheExport_StartsWithAUtf8Bom()
    {
        await SeedAsync();

        var response = await Client.GetAsync("/api/v1/payments/export");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Test]
    public async Task TheExport_HonoursTheSameFiltersAsTheList()
    {
        var wanted = await SeedAsync(PaymentStatus.Refunded, userId: "customer-9");
        await SeedAsync(PaymentStatus.Success, userId: "customer-9");
        await SeedAsync(PaymentStatus.Refunded, userId: "customer-1");

        var (_, body) = await ExportAsync("?status=Refunded&userId=customer-9");

        var lines = body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(2), "the header plus exactly one row");
            Assert.That(body, Does.Contain(wanted.Id.ToString()));
        });
    }

    /// <summary>
    /// A decline reason with a comma in it would shift every later column of that row, silently, in a file nobody
    /// reads by eye. This is the one field a customer's card issuer writes for us.
    /// </summary>
    [Test]
    public async Task AnErrorMessageWithACommaOrAQuote_DoesNotShiftTheColumns()
    {
        await SeedAsync(PaymentStatus.Failed, errorMessage: "Declined, \"insufficient funds\"");

        var (_, body) = await ExportAsync();

        var lines = body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(body, Does.Contain("\"Declined, \"\"insufficient funds\"\"\""));
        });
    }

    /// <summary>
    /// A refund reason is free text an operator types, and Excel evaluates a field opening with <c>=</c> as a
    /// formula — quoted or not. The reason is recorded as the payment's note (Payment audit D15), so this reaches the
    /// export by a path that exists today.
    /// </summary>
    [Test]
    public async Task ANoteThatASpreadsheetWouldRunAsAFormula_IsMadeInert()
    {
        await SeedAsync(PaymentStatus.Refunded, errorMessage: "=HYPERLINK(\"http://x\")");

        var (_, body) = await ExportAsync();

        Assert.That(body, Does.Contain("\"'=HYPERLINK"));
    }

    /// <summary>
    /// A silently shortened accounting export is a wrong answer that looks exactly like a right one. It refuses, and
    /// says by how much, so the operator narrows the window.
    /// </summary>
    [Test]
    public async Task AnExportLargerThanTheCap_IsRefused_RatherThanTruncated()
    {
        await SeedManyAsync(ExportPaymentsQuery.MaxRows + 1);

        var (response, body) = await ExportAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain("EXPORT_TOO_LARGE"));
            Assert.That(body, Does.Contain(ExportPaymentsQuery.MaxRows.ToString()));
        });
    }

    /// <summary>The same dataset, narrowed by a filter, is under the cap and exports normally.</summary>
    [Test]
    public async Task TheCapIsOnTheFilteredCount_NotTheTable()
    {
        await SeedManyAsync(ExportPaymentsQuery.MaxRows + 1);
        await SeedAsync(PaymentStatus.Refunded, userId: "customer-rare");

        var (response, body) = await ExportAsync("?userId=customer-rare");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public async Task AnEmptyExport_IsTheHeaderAlone()
    {
        var (response, body) = await ExportAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(1));
        });
    }

    [TestCase("?status=Payed")]
    [TestCase("?paymentMethod=Cheque")]
    [TestCase("?minAmount=50&maxAmount=5")]
    public async Task AnUnusableFilter_IsABadRequest(string query)
    {
        var (response, _) = await ExportAsync(query);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>Bulk-inserted in one save: seeding 10 001 rows one at a time through the API factory is minutes.</summary>
    private async Task SeedManyAsync(int count)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        for (var i = 0; i < count; i++)
        {
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                UserId = "customer-bulk",
                Amount = 1m,
                Currency = "USD",
                PaymentMethod = PaymentMethodType.Stripe,
                PaymentIntentId = $"pi_bulk_{i}",
                Status = PaymentStatus.Success,
                CreatedAt = Base,
                UpdatedAt = Base
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Kept here rather than read off the writer, so a silent column rename fails this suite too.</summary>
    private static readonly string[] PaymentCsvColumns =
    [
        "Id", "OrderId", "UserId", "Amount", "Currency", "PaymentMethod", "Status", "PaymentIntentId",
        "ErrorMessage", "CreatedAt", "ProcessedAt"
    ];
}
