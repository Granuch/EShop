using EShop.ApiGateway.AuditLog;
using EShop.BuildingBlocks.Infrastructure.Auditing;

namespace EShop.ApiGateway.UnitTests.AuditLog;

/// <summary>
/// Admin panel S15. The gateway's merge of the services' audit pages, and the per-service cursor that resumes it.
/// The property that matters is completeness: walking the merged trail page by page must return every row of every
/// service exactly once.
/// </summary>
[TestFixture]
public class AuditLogMergeTests
{
    private static readonly DateTime T0 = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static AuditLogEntryDto Row(string service, long id, int minute)
        => new(id, T0.AddMinutes(minute), service, "Act", "Thing", $"{service}-{id}", "admin-1", null, null, "Succeeded", null, "{}");

    /// <summary>A service's audit trail, newest (highest id) first, served the way <c>AuditLogReader</c> serves it.</summary>
    private sealed class FakeService(string name, params (long Id, int Minute)[] rows)
    {
        public string Name { get; } = name;

        public AuditLogPageDto Page(long before, int pageSize)
        {
            var remaining = rows.Where(r => before <= 0 || r.Id < before).OrderByDescending(r => r.Id).ToList();
            var page = remaining.Take(pageSize).Select(r => Row(Name, r.Id, r.Minute)).ToList();
            return new AuditLogPageDto(page, remaining.Count > pageSize ? page[^1].Id : null);
        }
    }

    private static List<AuditLogEntryDto> WalkAll(int pageSize, params FakeService[] services)
    {
        var seen = new List<AuditLogEntryDto>();
        var cursor = AuditLogCursor.Start;

        for (var guard = 0; guard < 100; guard++)
        {
            var pages = services
                .Where(s => !cursor.Exhausted.Contains(s.Name))
                .ToDictionary(s => s.Name, s => (AuditLogPageDto?)s.Page(cursor.Before.GetValueOrDefault(s.Name), pageSize));

            var (items, next) = AuditLogMerge.Merge(pageSize, cursor, pages);
            Assert.That(items, Has.Count.LessThanOrEqualTo(pageSize));
            seen.AddRange(items);

            if (services.All(s => next.Exhausted.Contains(s.Name)))
            {
                return seen;
            }

            // Round-trip every cursor, as a client does.
            Assert.That(AuditLogCursor.TryDecode(next.Encode(), services.Select(s => s.Name).ToList(), out cursor), Is.True);
        }

        Assert.Fail("the walk did not finish");
        return seen;
    }

    [Test]
    public void AMergedPage_IsNewestFirst_AcrossServices()
    {
        var catalog = new FakeService("catalog", (3, 30), (2, 10), (1, 5));
        var identity = new FakeService("identity", (9, 20), (8, 1));

        var pages = new Dictionary<string, AuditLogPageDto?>
        {
            ["catalog"] = catalog.Page(0, 10),
            ["identity"] = identity.Page(0, 10)
        };

        var (items, _) = AuditLogMerge.Merge(4, AuditLogCursor.Start, pages);

        Assert.That(items.Select(i => i.EntityId), Is.EqualTo(new[] { "catalog-3", "identity-9", "catalog-2", "catalog-1" }));
    }

    [Test]
    public void WalkingTheTrail_ReturnsEveryRowOfEveryServiceExactlyOnce()
    {
        var services = new[]
        {
            new FakeService("catalog", Enumerable.Range(1, 17).Select(i => ((long)i, i * 3)).ToArray()),
            new FakeService("identity", Enumerable.Range(100, 5).Select(i => ((long)i, i - 90)).ToArray()),
            new FakeService("payment")
        };

        var seen = WalkAll(4, services);

        Assert.That(seen.Select(s => s.EntityId), Is.Unique);
        Assert.That(seen, Has.Count.EqualTo(22));
    }

    [Test]
    public void ARowStampedLaterUnderASmallerId_IsNeverSkipped()
    {
        // Two writers in one service: id 5 committed first but was stamped a minute EARLIER than id 4. The service lists
        // by id (5, then 4), which is not time order. A union sorted by time would take id 4 before id 5, move the cursor
        // to "below 4", and never return id 5. The head-by-head merge takes a prefix of each list, so both arrive.
        var catalog = new FakeService("catalog", (5, 10), (4, 12), (3, 2));
        var identity = new FakeService("identity", (9, 11));

        var seen = WalkAll(2, catalog, identity);

        Assert.That(seen.Select(s => s.EntityId),
            Is.EquivalentTo(new[] { "catalog-5", "catalog-4", "catalog-3", "identity-9" }));
    }

    [Test]
    public void AServiceThatCouldNotBeRead_KeepsItsPosition_AndIsNotExhausted()
    {
        var cursor = new AuditLogCursor(
            new Dictionary<string, long> { ["payment"] = 40 }, new HashSet<string>());
        var pages = new Dictionary<string, AuditLogPageDto?>
        {
            ["catalog"] = new FakeService("catalog", (2, 1)).Page(0, 5),
            ["payment"] = null
        };

        var (items, next) = AuditLogMerge.Merge(5, cursor, pages);

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.Service), Is.All.EqualTo("catalog"));
            Assert.That(next.Before["payment"], Is.EqualTo(40), "its rows below 40 are still owed");
            Assert.That(next.Exhausted, Does.Not.Contain("payment"));
            Assert.That(next.Exhausted, Does.Contain("catalog"));
        });
    }

    [Test]
    public void AServiceWithMoreRows_IsResumedBelowTheLastRowTaken_NotBelowItsPageEnd()
    {
        var catalog = new FakeService("catalog", (6, 60), (5, 50), (4, 40), (3, 30));
        var identity = new FakeService("identity", (9, 55), (8, 45));
        var pages = new Dictionary<string, AuditLogPageDto?>
        {
            ["catalog"] = catalog.Page(0, 3),
            ["identity"] = identity.Page(0, 3)
        };

        var (items, next) = AuditLogMerge.Merge(3, AuditLogCursor.Start, pages);

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.EntityId), Is.EqualTo(new[] { "catalog-6", "identity-9", "catalog-5" }));
            Assert.That(next.Before["catalog"], Is.EqualTo(5), "catalog-4 was fetched but not taken, so it is still owed");
            Assert.That(next.Before["identity"], Is.EqualTo(9));
        });
    }

    // ---------- cursor ----------

    private static readonly string[] Known = ["catalog", "identity", "payment"];

    [Test]
    public void ACursor_RoundTrips()
    {
        var cursor = new AuditLogCursor(
            new Dictionary<string, long> { ["catalog"] = 12, ["identity"] = 3 }, new HashSet<string> { "payment" });

        Assert.That(AuditLogCursor.TryDecode(cursor.Encode(), Known, out var decoded), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(decoded.Before, Is.EquivalentTo(cursor.Before));
            Assert.That(decoded.Exhausted, Is.EquivalentTo(cursor.Exhausted));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void NoCursor_IsTheStart(string? value)
    {
        Assert.That(AuditLogCursor.TryDecode(value, Known, out var cursor), Is.True);
        Assert.That(cursor.Before, Is.Empty);
    }

    [TestCase("not base64 at all!")]
    [TestCase("bm90IGpzb24")] // "not json"
    public void Garbage_IsRefused(string value)
        => Assert.That(AuditLogCursor.TryDecode(value, Known, out _), Is.False);

    [Test]
    public void ACursorNamingAnUnknownService_IsRefused()
    {
        var forged = new AuditLogCursor(new Dictionary<string, long> { ["basket"] = 5 }, new HashSet<string>()).Encode();
        Assert.That(AuditLogCursor.TryDecode(forged, Known, out _), Is.False);
    }

    [Test]
    public void ACursorWithANonPositiveId_IsRefused()
    {
        var forged = new AuditLogCursor(new Dictionary<string, long> { ["catalog"] = 0 }, new HashSet<string>()).Encode();
        Assert.That(AuditLogCursor.TryDecode(forged, Known, out _), Is.False);
    }

    [Test]
    public void ACursorBothResumingAndFinishingAService_IsRefused()
    {
        var forged = new AuditLogCursor(
            new Dictionary<string, long> { ["catalog"] = 5 }, new HashSet<string> { "catalog" }).Encode();
        Assert.That(AuditLogCursor.TryDecode(forged, Known, out _), Is.False);
    }
}
