using EShop.Identity.Application.Roles.Queries.GetRoles;
using EShop.Identity.Application.Roles.Queries.GetUsersInRole;

namespace EShop.Identity.UnitTests.Admin;

/// <summary>
/// Frontend-contracts F-12. Both role lists had no validator, so <c>page=0</c> or a negative size
/// reached Postgres as a negative <c>OFFSET</c>/<c>LIMIT</c> (500) and <c>pageSize=100000</c> was served.
/// </summary>
[TestFixture]
public class RolePagingValidatorTests
{
    [TestCase(null, null, true)]
    [TestCase(1, 1, true)]
    [TestCase(3, 100, true)]
    [TestCase(0, null, false)]
    [TestCase(-1, null, false)]
    [TestCase(null, 0, false)]
    [TestCase(null, -1, false)]
    [TestCase(null, 101, false)]
    public void BothLists_ShareOnePagingRule(int? pageNumber, int? pageSize, bool valid)
    {
        var roles = new GetRolesQueryValidator().Validate(
            new GetRolesQuery { PageNumber = pageNumber, PageSize = pageSize });
        var members = new GetUsersInRoleQueryValidator().Validate(
            new GetUsersInRoleQuery { RoleName = "Admin", PageNumber = pageNumber, PageSize = pageSize });

        Assert.That(roles.IsValid, Is.EqualTo(valid), "GET /roles");
        Assert.That(members.IsValid, Is.EqualTo(valid), "GET /roles/{roleName}/users");
    }

    [Test]
    public void AFailure_IsKeyedByTheParameterName()
    {
        var result = new GetRolesQueryValidator().Validate(new GetRolesQuery { PageNumber = 0, PageSize = 101 });

        Assert.That(
            result.Errors.Select(e => e.PropertyName).OrderBy(n => n),
            Is.EqualTo(new[] { "PageNumber", "PageSize" }));
    }
}
