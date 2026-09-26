using EShop.Identity.Application.Users.Queries.GetAdminUsers;
using EShop.Identity.Application.Users.Queries.GetAdminUserStats;
using EShop.Identity.Domain.Interfaces;
using Moq;

namespace EShop.Identity.UnitTests.Admin;

/// <summary>
/// Frontend-contracts F-15. A date bound from the query string without <c>Z</c> or an offset is
/// <see cref="DateTimeKind.Unspecified"/>, which Npgsql refuses as a <c>timestamptz</c> parameter, so
/// the handlers must hand the query service UTC values. The Postgres half (the 500 this prevents) is
/// <c>AdminUserReadsTests.ADateWithNoTimeZone_…</c>.
/// </summary>
[TestFixture]
public class AdminUserDateFilterTests
{
    private static readonly DateTime Unspecified = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTime Local = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Local);

    [Test]
    public async Task TheList_HandsTheQueryServiceUtcDates()
    {
        var queryService = new Mock<IAdminUserQueryService>();
        AdminUserFilter? seen = null;
        queryService
            .Setup(x => x.GetUsersAsync(
                It.IsAny<AdminUserFilter>(), It.IsAny<AdminUserSortBy>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<AdminUserFilter, AdminUserSortBy, bool, int, int, CancellationToken>(
                (filter, _, _, _, _, _) => seen = filter)
            .ReturnsAsync((Array.Empty<AdminUserRow>(), 0));

        await new GetAdminUsersQueryHandler(queryService.Object).Handle(
            new GetAdminUsersQuery
            {
                CreatedFrom = Unspecified,
                CreatedTo = Local,
                LastLoginFrom = Unspecified,
                LastLoginTo = Unspecified
            },
            CancellationToken.None);

        Assert.That(seen, Is.Not.Null);
        Assert.That(
            new[] { seen!.CreatedFrom, seen.CreatedTo, seen.LastLoginFrom, seen.LastLoginTo }.Select(d => d!.Value.Kind),
            Is.EqualTo(new[] { DateTimeKind.Utc, DateTimeKind.Utc, DateTimeKind.Utc, DateTimeKind.Utc }));
        Assert.That(seen.CreatedFrom, Is.EqualTo(Unspecified), "an unzoned date is read as UTC, not shifted");
        Assert.That(seen.CreatedTo, Is.EqualTo(Local.ToUniversalTime()), "an offset is converted");
    }

    [Test]
    public async Task TheStats_HandTheQueryServiceUtcDates()
    {
        var queryService = new Mock<IAdminUserQueryService>();
        DateTime? from = null, to = null;
        queryService
            .Setup(x => x.GetUserStatsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime?, DateTime?, CancellationToken>((f, t, _) => (from, to) = (f, t))
            .ReturnsAsync(new AdminUserStats(0, 0, 0, 0, 0, 0));

        await new GetAdminUserStatsQueryHandler(queryService.Object).Handle(
            new GetAdminUserStatsQuery { From = Unspecified, To = Unspecified },
            CancellationToken.None);

        Assert.That(from!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(to!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
    }
}
