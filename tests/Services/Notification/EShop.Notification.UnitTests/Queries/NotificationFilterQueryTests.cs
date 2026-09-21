using EShop.Notification.Application.Notifications.Queries.GetNotifications;
using EShop.Notification.Domain.Entities;

namespace EShop.Notification.UnitTests.Queries;

/// <summary>
/// Admin panel S12. What the query record hands the query service. The UTC coercion is the part that matters:
/// <c>?from=2026-09-01</c> — what an admin URL actually looks like — binds to a <c>DateTime</c> with
/// <c>Kind.Unspecified</c>, and Npgsql refuses to send one as a <c>timestamp with time zone</c> parameter, so without
/// this the most obvious filter on the screen is a 500 rather than a filter.
/// <para>This pins that we coerce. <c>Persistence/NotificationJournalSqlTests</c> pins why it is necessary, because the
/// HTTP suite runs on EF InMemory, where a <c>DateTime</c> comparison ignores <c>Kind</c> entirely.</para>
/// </summary>
[TestFixture]
public class NotificationFilterQueryTests
{
    [Test]
    public void ADateWithNoTimeZone_IsReadAsUtc()
    {
        var query = new GetNotificationsQuery
        {
            From = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Unspecified),
            To = new DateTime(2026, 9, 2, 8, 30, 0, DateTimeKind.Unspecified)
        };

        var filter = query.ToFilter();

        Assert.Multiple(() =>
        {
            Assert.That(filter.From!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(filter.From!.Value, Is.EqualTo(new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc)),
                "the instant must be taken at face value, not shifted by the server's time zone");
            Assert.That(filter.To!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void ALocalDate_IsConverted_NotRelabelled()
    {
        var local = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Local);

        var filter = new GetNotificationsQuery { From = local }.ToFilter();

        Assert.That(filter.From, Is.EqualTo(local.ToUniversalTime()));
    }

    [Test]
    public void AnOmittedBound_StaysOmitted()
    {
        var filter = new GetNotificationsQuery().ToFilter();

        Assert.Multiple(() =>
        {
            Assert.That(filter.From, Is.Null);
            Assert.That(filter.To, Is.Null);
            Assert.That(filter.Statuses, Is.Empty);
            Assert.That(filter.HasError, Is.Null);
        });
    }

    [Test]
    public void StatusNames_AreParsedCaseInsensitively_AndDeduplicated()
    {
        var filter = new GetNotificationsQuery { Status = ["failed", "FAILED", "Undeliverable"] }.ToFilter();

        Assert.That(filter.Statuses,
            Is.EquivalentTo(new[] { NotificationStatus.Failed, NotificationStatus.Undeliverable }));
    }
}
