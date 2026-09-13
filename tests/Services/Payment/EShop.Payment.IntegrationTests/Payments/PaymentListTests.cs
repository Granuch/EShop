using System.Net;
using System.Net.Http.Json;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Payment audit Stage 10 (M7; D10, D11). <c>GET /api/v1/users/{userId}/payments</c> is paged like Ordering's per-user
/// order list, newest first. It skips the placeholder a cancellation leaves when it overtakes the order. It used to
/// return every payment the user ever had, placeholders included. An admin may list any user, so each test lists a user
/// of its own.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PaymentListTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-1";

    private static string NewUser() => $"customer-{Guid.NewGuid():N}";

    private async Task<PaymentTransaction> SeedAsync(string userId, DateTime createdAt)
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = userId,
            Amount = 10m,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Mock,
            PaymentIntentId = $"pi_{Guid.NewGuid():N}",
            Status = PaymentStatus.Success,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
        await Factory.SeedAsync(payment);

        // BaseDbContext stamps CreatedAt with the save time on insert, whatever was set, so the time is set by a second
        // save: on an update only UpdatedAt is stamped.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var stored = await db.PaymentTransactions.SingleAsync(p => p.Id == payment.Id);
        stored.CreatedAt = createdAt;
        await db.SaveChangesAsync();

        return payment;
    }

    private async Task<Page> ListAsync(string userId, string query = "")
    {
        var response = await Client.GetAsync($"/api/v1/users/{userId}/payments{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Page>())!;
    }

    [Test]
    public async Task AUsersPayments_ArePaged_NewestFirst()
    {
        var user = NewUser();
        var now = DateTime.UtcNow;
        var oldest = await SeedAsync(user, now.AddHours(-3));
        var newest = await SeedAsync(user, now.AddHours(-1));
        var middle = await SeedAsync(user, now.AddHours(-2));

        var first = await ListAsync(user, "?pageNumber=1&pageSize=2");
        var second = await ListAsync(user, "?pageNumber=2&pageSize=2");

        Assert.Multiple(() =>
        {
            Assert.That(first.Items.Select(p => p.Id), Is.EqualTo(new[] { newest.Id, middle.Id }));
            Assert.That(first.TotalCount, Is.EqualTo(3));
            Assert.That(first.TotalPages, Is.EqualTo(2));
            Assert.That(first.HasNextPage, Is.True);
            Assert.That(first.HasPreviousPage, Is.False);

            Assert.That(second.Items.Select(p => p.Id), Is.EqualTo(new[] { oldest.Id }));
            Assert.That(second.PageNumber, Is.EqualTo(2));
            Assert.That(second.HasNextPage, Is.False);
            Assert.That(second.HasPreviousPage, Is.True);
        });
    }

    [Test]
    public async Task WithNoPageGiven_TheNewestTenAreReturned()
    {
        var user = NewUser();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 11; i++)
        {
            await SeedAsync(user, now.AddMinutes(-i));
        }

        var page = await ListAsync(user);

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Has.Count.EqualTo(10));
            Assert.That(page.TotalCount, Is.EqualTo(11));
            Assert.That(page.PageNumber, Is.EqualTo(1));
            Assert.That(page.PageSize, Is.EqualTo(10));
        });
    }

    /// <summary>
    /// D11. The placeholder stays in the database, where it stops a late OrderCreatedEvent from charging the order, but
    /// nothing was ever started or charged for it, so the customer's list and its total leave it out.
    /// </summary>
    [Test]
    public async Task ThePlaceholderOfAnOrderCancelledBeforeItsPayment_IsNotListed()
    {
        var user = NewUser();
        var paid = await SeedAsync(user, DateTime.UtcNow.AddHours(-1));
        await Factory.SeedAsync(PaymentTransaction.RecordCancelledBeforeCreation(
            Guid.NewGuid(), user, "Order cancelled before its payment was recorded.", DateTime.UtcNow));

        var page = await ListAsync(user);

        Assert.Multiple(() =>
        {
            Assert.That(page.Items.Select(p => p.Id), Is.EqualTo(new[] { paid.Id }));
            Assert.That(page.TotalCount, Is.EqualTo(1));
        });
    }

    [TestCase("?pageSize=0")]
    [TestCase("?pageSize=101")]
    [TestCase("?pageNumber=0")]
    public async Task AnOutOfRangePage_IsABadRequest(string query)
    {
        var response = await Client.GetAsync($"/api/v1/users/{NewUser()}/payments{query}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private sealed record Page(
        List<PaymentResponse> Items,
        int PageNumber,
        int PageSize,
        int TotalCount,
        int TotalPages,
        bool HasPreviousPage,
        bool HasNextPage);

    private sealed record PaymentResponse(Guid Id, string UserId, string PaymentMethod, string Status);
}
