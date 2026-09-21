using EShop.Notification.Application.Notifications.Queries.GetNotifications;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.QueryServices;
using EShop.Notification.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EShop.Notification.IntegrationTests.Persistence;

/// <summary>
/// The half of the journal that EF InMemory cannot show (Admin panel S12), on real PostgreSQL.
///
/// <para>
/// Two things live here and nowhere else. <b>The UTC coercion:</b> InMemory compares <c>DateTime</c> values while
/// ignoring <c>Kind</c> entirely, so deleting <c>NotificationQueryEnums.AsUtc</c> leaves every HTTP date test green —
/// only Npgsql refuses an <c>Unspecified</c> value as a <c>timestamp with time zone</c> parameter, and
/// <see cref="Npgsql_RefusesADateWithNoTimeZone_WhichIsWhyTheHandlerCoercesIt"/> pins why the coercion has to exist.
/// <b>Seeded dates:</b> <c>BaseDbContext</c> overwrites <c>CreatedAt</c> on every insert, so a chosen date needs
/// <c>ExecuteUpdate</c>, which is relational-only.
/// </para>
/// </summary>
[TestFixture]
public class NotificationJournalSqlTests
{
    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    [Test]
    public async Task Npgsql_RefusesADateWithNoTimeZone_WhichIsWhyTheHandlerCoercesIt()
    {
        var seeded = await SeedAsync("OrderCreatedEvent", new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc));

        // Exactly what ?from=2026-09-01 binds to: an admin URL with no Z and no offset.
        var unspecified = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);

        await using var db = NewContext();
        var service = new NotificationQueryService(db);

        Assert.That(
            async () => await service.GetPageAsync(
                NotificationJournalFilter.All with { From = unspecified }, 1, 10),
            Throws.Exception,
            "if Npgsql ever stops refusing this, AsUtc becomes optional — and this test is how you find out");

        // The control: the same instant, coerced the way the query record does it, is an ordinary filter that answers.
        var (items, _) = await service.GetPageAsync(
            NotificationJournalFilter.All with { From = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc) },
            1,
            10);

        Assert.That(items.Select(x => x.Id), Is.EqualTo(new[] { seeded }),
            "control: the only difference between the two calls is the Kind of the bound");
    }

    [Test]
    public async Task TheQueryRecord_CoercesTheBound_SoAnUnqualifiedDateIsAFilterRatherThanAFailure()
    {
        var recent = await SeedAsync("OrderCreatedEvent", new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc));
        await SeedAsync("OrderShippedEvent", new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));

        var query = new GetNotificationsQuery
        {
            From = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified)
        };

        await using var db = NewContext();
        var (items, totalCount) = await new NotificationQueryService(db).GetPageAsync(query.ToFilter(), 1, 10);

        totalCount.Should().Be(1);
        items.Single().Id.Should().Be(recent);
    }

    [Test]
    public async Task TheList_IsNewestFirst_AcrossPages()
    {
        var oldest = await SeedAsync("A", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        var middle = await SeedAsync("B", new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc));
        var newest = await SeedAsync("C", new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc));

        await using var db = NewContext();
        var service = new NotificationQueryService(db);

        var (first, total) = await service.GetPageAsync(NotificationJournalFilter.All, 1, 2);
        var (second, _) = await service.GetPageAsync(NotificationJournalFilter.All, 2, 2);

        total.Should().Be(3);
        first.Select(x => x.Id).Should().Equal(newest, middle);
        second.Select(x => x.Id).Should().Equal(oldest);
    }

    [Test]
    public async Task RowsSharingAnInstant_AreOrderedById_SoNoneAppearsOnTwoPagesOrOnNone()
    {
        // Offset paging over a non-unique sort is nondeterministic on Postgres. Three rows to the same tick is what a
        // burst of consumers produces, and without the Id tie-break page 2 can repeat a row page 1 already showed.
        var instant = new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc);
        var ids = new List<Guid>
        {
            await SeedAsync("A", instant),
            await SeedAsync("B", instant),
            await SeedAsync("C", instant)
        };

        await using var db = NewContext();
        var service = new NotificationQueryService(db);

        var (first, _) = await service.GetPageAsync(NotificationJournalFilter.All, 1, 2);
        var (second, _) = await service.GetPageAsync(NotificationJournalFilter.All, 2, 2);

        first.Select(x => x.Id).Concat(second.Select(x => x.Id))
            .Should().BeEquivalentTo(ids, "every row must appear exactly once across the pages");
        first.Select(x => x.Id).Should().Equal(ids.OrderByDescending(id => id).Take(2));
    }

    [Test]
    public async Task TheStats_CountOnlyTheWindow()
    {
        await SeedAsync("A", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        await SeedAsync("B", new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc));

        await using var db = NewContext();
        var stats = await new NotificationQueryService(db).GetStatsAsync(
            NotificationJournalFilter.All with
            {
                From = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc)
            });

        stats.Total.Should().Be(1);
        stats.ByStatus.Sum(s => s.Count).Should().Be(1);
    }

    private NotificationDbContext NewContext() => new(new DbContextOptionsBuilder<NotificationDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    /// <summary>
    /// Seeds one <c>Pending</c> row dated <paramref name="createdAt"/>.
    /// </summary>
    /// <remarks>
    /// The date is applied with <c>ExecuteUpdate</c> after the insert, not passed to the factory: <c>BaseDbContext</c>
    /// stamps <c>CreatedAt</c> to <c>DateTime.UtcNow</c> on every added entity, so a chosen value set before
    /// <c>SaveChanges</c> is silently replaced and the test would be asserting insertion order instead.
    /// </remarks>
    private async Task<Guid> SeedAsync(string eventType, DateTime createdAt)
    {
        await using var db = NewContext();
        var log = NotificationLog.CreatePending(Guid.NewGuid(), eventType, "corr", "user-1", "template", "subject");
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync();

        await db.NotificationLogs
            .Where(l => l.Id == log.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, createdAt));

        return log.Id;
    }
}
