using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.BuildingBlocks.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace EShop.BuildingBlocks.UnitTests.Auditing;

/// <summary>
/// Admin panel S15. The audit query's rules — shared by every service and the gateway — and the reader's keyset paging.
/// </summary>
[TestFixture]
public class AuditLogQueryTests
{
    // ---------- rules ----------

    [Test]
    public void AnEmptyRequest_IsAFirstPageOfTheDefaultSize()
    {
        Assert.That(AuditLogQueryRules.TryCreateFilter(new AuditLogRequest(), out var filter, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(filter.PageSize, Is.EqualTo(AuditLogQueryRules.DefaultPageSize));
            Assert.That(filter.Before, Is.Null);
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(AuditLogQueryRules.MaxPageSize + 1)]
    public void ThePageSize_IsBounded(int pageSize)
        => Assert.That(AuditLogQueryRules.TryCreateFilter(new AuditLogRequest { PageSize = pageSize }, out _, out _), Is.False);

    [Test]
    public void TheLargestPage_IsAllowed()
        => Assert.That(
            AuditLogQueryRules.TryCreateFilter(new AuditLogRequest { PageSize = AuditLogQueryRules.MaxPageSize }, out _, out _),
            Is.True);

    [TestCase(0L)]
    [TestCase(-5L)]
    public void Before_MustBeAPositiveId(long before)
        => Assert.That(AuditLogQueryRules.TryCreateFilter(new AuditLogRequest { Before = before }, out _, out _), Is.False);

    [TestCase("rejected", AuditOutcome.Rejected)]
    [TestCase(" Failed ", AuditOutcome.Failed)]
    [TestCase("SUCCEEDED", AuditOutcome.Succeeded)]
    public void TheOutcome_IsReadByName_IgnoringCase(string value, AuditOutcome expected)
    {
        Assert.That(AuditLogQueryRules.TryCreateFilter(new AuditLogRequest { Outcome = value }, out var filter, out _), Is.True);
        Assert.That(filter.Outcome, Is.EqualTo(expected));
    }

    [TestCase("1")]
    [TestCase("7")]
    [TestCase("deleted")]
    public void AnOutcomeThatIsNotAName_IsRefused(string value)
        => Assert.That(AuditLogQueryRules.TryCreateFilter(new AuditLogRequest { Outcome = value }, out _, out _), Is.False);

    [Test]
    public void FromMustBeEarlierThanTo()
    {
        var at = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.That(AuditLogQueryRules.TryCreateFilter(new AuditLogRequest { From = at, To = at }, out _, out _), Is.False);
    }

    [Test]
    public void ADateWithNoZone_IsReadAsUtc()
    {
        var unspecified = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.That(AuditLogQueryRules.TryCreateFilter(new AuditLogRequest { From = unspecified }, out var filter, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(filter.From!.Value.Kind, Is.EqualTo(DateTimeKind.Utc), "Npgsql refuses an Unspecified timestamptz");
            Assert.That(filter.From.Value.Ticks, Is.EqualTo(unspecified.Ticks));
        });
    }

    [Test]
    public void AnOverlongFilter_IsRefused_RatherThanMatchingNothing()
        => Assert.That(
            AuditLogQueryRules.TryCreateFilter(
                new AuditLogRequest { EntityId = new string('x', AuditLogEntry.EntityIdMaxLength + 1) }, out _, out _),
            Is.False);

    [Test]
    public void BlankFilters_AreIgnored()
    {
        Assert.That(
            AuditLogQueryRules.TryCreateFilter(new AuditLogRequest { Action = "  ", EntityId = "" }, out var filter, out _),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(filter.Action, Is.Null);
            Assert.That(filter.EntityId, Is.Null);
        });
    }

    // ---------- reader ----------

    private static ReaderDbContext Seed(int count, Func<int, (string Actor, string EntityId, AuditOutcome Outcome)>? shape = null)
    {
        var db = new ReaderDbContext(new DbContextOptionsBuilder<ReaderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        for (var i = 1; i <= count; i++)
        {
            var (actor, entityId, outcome) = shape?.Invoke(i) ?? ("admin-1", $"e{i}", AuditOutcome.Succeeded);
            db.Set<AuditLogEntry>().Add(AuditLogEntry.Record(
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                "svc", "Act", "Widget", entityId, actor, null, null, outcome, null, "{}"));
            db.SaveChanges();
        }

        return db;
    }

    private static AuditLogFilter Filter(int pageSize = 50, long? before = null, string? actor = null,
        string? entityId = null, AuditOutcome? outcome = null, DateTime? from = null, DateTime? to = null)
        => new(before, pageSize, actor, null, null, entityId, outcome, from, to);

    [Test]
    public async Task Pages_AreNewestFirst_AndWalkEveryRowExactlyOnce()
    {
        await using var db = Seed(7);
        var reader = new AuditLogReader<ReaderDbContext>(db);

        var seen = new List<long>();
        long? before = null;
        var pages = 0;
        do
        {
            var page = await reader.ReadAsync(Filter(pageSize: 3, before: before), CancellationToken.None);
            Assert.That(page.Items, Has.Count.LessThanOrEqualTo(3));
            seen.AddRange(page.Items.Select(i => i.Id));
            before = page.NextBefore;
            pages++;
        }
        while (before is not null && pages < 10);

        Assert.Multiple(() =>
        {
            Assert.That(pages, Is.EqualTo(3));
            Assert.That(seen, Is.EqualTo(seen.OrderByDescending(id => id).ToList()), "newest first");
            Assert.That(seen, Is.Unique);
            Assert.That(seen, Has.Count.EqualTo(7));
        });
    }

    [Test]
    public async Task AFullLastPage_ReportsNoNextPage()
    {
        await using var db = Seed(4);
        var reader = new AuditLogReader<ReaderDbContext>(db);

        var first = await reader.ReadAsync(Filter(pageSize: 2), CancellationToken.None);
        var second = await reader.ReadAsync(Filter(pageSize: 2, before: first.NextBefore), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.NextBefore, Is.EqualTo(first.Items[^1].Id));
            Assert.That(second.Items, Has.Count.EqualTo(2));
            Assert.That(second.NextBefore, Is.Null, "exactly the page size left is the end, not another empty page");
        });
    }

    [Test]
    public async Task EachFilter_NarrowsTheRows()
    {
        await using var db = Seed(6, i => (i % 2 == 0 ? "admin-2" : "admin-1", $"e{i % 3}",
            i == 5 ? AuditOutcome.Failed : AuditOutcome.Succeeded));
        var reader = new AuditLogReader<ReaderDbContext>(db);

        var byActor = await reader.ReadAsync(Filter(actor: "admin-2"), CancellationToken.None);
        var byEntity = await reader.ReadAsync(Filter(entityId: "e1"), CancellationToken.None);
        var byOutcome = await reader.ReadAsync(Filter(outcome: AuditOutcome.Failed), CancellationToken.None);
        var byWindow = await reader.ReadAsync(
            Filter(from: new DateTime(2026, 9, 1, 0, 2, 0, DateTimeKind.Utc), to: new DateTime(2026, 9, 1, 0, 4, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(byActor.Items.Select(i => i.ActorUserId), Is.EqualTo(new[] { "admin-2", "admin-2", "admin-2" }));
            Assert.That(byEntity.Items.Select(i => i.EntityId), Is.EqualTo(new[] { "e1", "e1" }));
            Assert.That(byOutcome.Items.Select(i => i.Outcome), Is.EqualTo(new[] { "Failed" }));
            // From is inclusive, To exclusive: minutes 2 and 3.
            Assert.That(byWindow.Items.Select(i => i.OccurredAt.Minute), Is.EquivalentTo(new[] { 2, 3 }));
        });
    }

    private sealed class ReaderDbContext(DbContextOptions<ReaderDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
    }
}
