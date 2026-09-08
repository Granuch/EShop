using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.UnitTests.Persistence;

/// <summary>
/// Pins the soft-delete global query filter on <see cref="ApplicationUser"/>.
///
/// <para>
/// This lived in <c>GetUserByEmailQueryHandlerTests</c> purely by accident of history, and moved
/// here when DEBT-19 deleted that dead query. It has nothing to do with any one handler: the
/// filter is what makes <c>IsDeleted</c> enforced rather than advisory. Six handlers used to
/// re-check it by hand and five did not, and <c>UserStore</c> queries the same <c>DbSet</c>, so
/// <c>UserManager</c> inherits the filter and a soft-deleted user is simply never found.
/// </para>
///
/// <para>
/// It is asserted at the model level on purpose. A mocked <c>UserManager</c> has no EF behind it
/// and therefore cannot exercise the filter at all — a unit test that hands back a soft-deleted
/// user is testing a state the real system cannot produce. Removing the filter would silently
/// restore the old hand-checked behaviour everywhere at once, so the model is the only place a
/// cheap test can catch it.
/// </para>
/// </summary>
[TestFixture]
public class IdentityDbContextFilterTests
{
    [Test]
    public void ApplicationUser_HasASoftDeleteQueryFilter()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"filter-check-{Guid.NewGuid()}")
            .Options;

        using var context = new IdentityDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(ApplicationUser));

        Assert.That(entityType, Is.Not.Null);

        var filters = entityType!.GetDeclaredQueryFilters();

        Assert.That(filters, Is.Not.Null.And.Not.Empty,
            "ApplicationUser must keep its soft-delete query filter; without it IsDeleted is "
            + "advisory again and every handler has to re-check it by hand.");
        Assert.That(
            string.Join(" ", filters.Select(f => f.Expression?.ToString())),
            Does.Contain(nameof(ApplicationUser.IsDeleted)));
    }
}
