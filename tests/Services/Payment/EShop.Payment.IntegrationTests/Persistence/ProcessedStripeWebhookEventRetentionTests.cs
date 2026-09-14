using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.Infrastructure.Services;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace EShop.Payment.IntegrationTests.Persistence;

/// <summary>
/// Payment audit Stage 10 (D13), on Postgres. The cleanup deletes processed webhook events older than 30 days, and only
/// those. The bulk delete cannot run on the InMemory provider.
/// </summary>
[TestFixture]
public class ProcessedStripeWebhookEventRetentionTests
{
    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private static ProcessedStripeWebhookEvent Event(string id, DateTime processedAt) => new()
    {
        Id = Guid.NewGuid(),
        EventId = id,
        EventType = "payment_intent.succeeded",
        ProcessedAt = processedAt
    };

    [Test]
    public async Task OnlyEventsOlderThanTheRetention_AreDeleted()
    {
        var now = DateTime.UtcNow;
        await using (var db = NewContext())
        {
            db.ProcessedStripeWebhookEvents.AddRange(
                Event("evt_31_days", now.AddDays(-31)),
                Event("evt_29_days", now.AddDays(-29)),
                Event("evt_today", now));
            await db.SaveChangesAsync();
        }

        int deleted;
        await using (var db = NewContext())
        {
            deleted = await new PaymentRepository(db).DeleteProcessedStripeEventsBeforeAsync(
                ProcessedStripeWebhookEventCleanupService.CutoffFor(now));
        }

        await using var check = NewContext();
        var kept = await check.ProcessedStripeWebhookEvents.OrderBy(e => e.EventId).Select(e => e.EventId).ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(kept, Is.EqualTo(new[] { "evt_29_days", "evt_today" }));
        });
    }
}
