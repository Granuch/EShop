using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Helpers;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// Admin panel S6 — the read-only user screens: list, detail, roles, sessions and the dashboard
/// tile. Identity had no way to enumerate users at all before this; <c>UsersController</c> exposed
/// one internal-service contact lookup by id.
/// </summary>
[TestFixture]
[Category("Integration")]
public class AdminUserReadsTests : AuthenticatedIntegrationTestBase
{
    private const string Endpoint = "/api/v1/admin/users";

    private async Task<AdminUserPage> ListAsync(string query = "")
    {
        var response = await Client.GetAsync($"{Endpoint}{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AdminUserPage>())!;
    }

    private async Task<string> AdminUserIdAsync()
    {
        var page = await ListAsync($"?search={TestUsers.Admin.Email}&pageSize=10");
        page.Items.Should().NotBeEmpty();
        return page.Items.Single(u => u.Email == TestUsers.Admin.Email).Id;
    }

    #region List (#1)

    [Test]
    public async Task List_ReturnsSeededUsers_WithTheirRoles()
    {
        var page = await ListAsync("?pageSize=100");

        page.TotalCount.Should().BeGreaterThan(0);
        var admin = page.Items.Single(u => u.Email == TestUsers.Admin.Email);
        admin.Roles.Should().Contain(TestUsers.Admin.Role,
            "roles come from one batch query per page, not one per row");
        admin.FirstName.Should().Be(TestUsers.Admin.FirstName);
    }

    [Test]
    public async Task List_FiltersBySearch_AcrossEmailAndName()
    {
        var byEmail = await ListAsync($"?search={TestUsers.RegularUser.Email}&pageSize=100");
        byEmail.Items.Should().OnlyContain(u => u.Email == TestUsers.RegularUser.Email);

        var byFirstName = await ListAsync($"?search={TestUsers.RegularUser.FirstName}&pageSize=100");
        byFirstName.Items.Should().Contain(u => u.Email == TestUsers.RegularUser.Email);
    }

    [Test]
    public async Task List_SearchIsCaseInsensitive()
    {
        var upper = await ListAsync($"?search={TestUsers.RegularUser.Email.ToUpperInvariant()}&pageSize=100");

        upper.Items.Should().Contain(u => u.Email == TestUsers.RegularUser.Email,
            "the filter uses ILIKE, so an admin need not match the stored casing");
    }

    [Test]
    public async Task List_SearchDoesNotAllowWildcardInjection()
    {
        // A bare % would match every user if the term were not escaped.
        var page = await ListAsync("?search=%25&pageSize=100");

        page.Items.Should().BeEmpty("'%' must be escaped into a literal, not treated as a wildcard");
    }

    [Test]
    public async Task List_FiltersByRole()
    {
        var admins = await ListAsync($"?role={TestUsers.Admin.Role}&pageSize=100");

        admins.Items.Should().NotBeEmpty();
        admins.Items.Should().OnlyContain(u => u.Roles.Contains(TestUsers.Admin.Role));
        admins.Items.Should().Contain(u => u.Email == TestUsers.Admin.Email);
    }

    [Test]
    public async Task List_FilteringByRole_DoesNotMultiplyRows()
    {
        // "One row per user, whatever their roles" — the property, not the implementation.
        //
        // Honest about what this does and does not prove. Falsification swapped the handler's
        // set-membership subquery for a Join against the same single-role-filtered membership and
        // this stayed green — correctly, because one role can match a user at most once, so the two
        // forms are equivalent today. What the test pins is the observable property, which would
        // break the moment the filter became "any of these roles" and someone reached for a Join.
        // The two-role user is here so that case is already covered when it arrives.
        var email = $"s6-tworoles-{Guid.NewGuid():N}@test.com";
        var userId = await UserManagementHelper.CreateTestUserAsync(
            Factory.Services, email, role: TestUsers.Admin.Role);
        await UserManagementHelper.AddRoleAsync(Factory.Services, userId, TestUsers.RegularUser.Role);

        var admins = await ListAsync($"?role={TestUsers.Admin.Role}&pageSize=100");

        admins.Items.Should().Contain(u => u.Id == userId, "precondition: the two-role user is in scope");
        admins.Items.Where(u => u.Id == userId).Should().HaveCount(1,
            "a user must appear once however many roles they hold");
        admins.Items.Select(u => u.Id).Should().OnlyHaveUniqueItems();
        admins.TotalCount.Should().Be(admins.Items.Count,
            "TotalCount is taken from the same query, so a multiplying join corrupts it too");
    }

    [Test]
    public async Task List_FiltersByIsActive()
    {
        var inactive = await ListAsync("?isActive=false&pageSize=100");

        inactive.Items.Should().OnlyContain(u => !u.IsActive);
        inactive.Items.Should().Contain(u => u.Email == TestUsers.InactiveUser.Email);
    }

    [Test]
    public async Task List_ExcludesDeletedUsersByDefault_AndIncludesThemOnlyWithTheFlag()
    {
        var email = $"s6-deleted-{Guid.NewGuid():N}@test.com";
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        await UserManagementHelper.SoftDeleteUserAsync(Factory.Services, userId);

        var live = await ListAsync($"?search={email}&pageSize=100");
        live.Items.Should().BeEmpty("the !IsDeleted global query filter applies by default");

        var deleted = await ListAsync($"?search={email}&isDeleted=true&pageSize=100");
        deleted.Items.Should().ContainSingle(u => u.Id == userId);
        deleted.Items.Single().IsDeleted.Should().BeTrue();
    }

    [Test]
    public async Task List_WithIsDeletedTrue_ReturnsOnlyDeletedUsers()
    {
        // IgnoreQueryFilters applies to the whole query, so the explicit IsDeleted predicate is
        // what scopes this — without it the list would widen to every user while still looking
        // like a "deleted users" filter.
        var page = await ListAsync("?isDeleted=true&pageSize=100");

        page.Items.Should().OnlyContain(u => u.IsDeleted);
        page.Items.Should().NotContain(u => u.Email == TestUsers.Admin.Email);
    }

    [Test]
    public async Task List_PagesAndCountsUnderTheSameFilter()
    {
        var all = await ListAsync("?pageSize=100");
        var firstPage = await ListAsync("?pageSize=1&pageNumber=1");
        var secondPage = await ListAsync("?pageSize=1&pageNumber=2");

        firstPage.Items.Should().HaveCount(1);
        firstPage.TotalCount.Should().Be(all.TotalCount, "the count must describe the filter, not the page");
        secondPage.Items.Single().Id.Should().NotBe(firstPage.Items.Single().Id,
            "Id is the tiebreaker on every sort, so pages cannot repeat a row");
    }

    [Test]
    public async Task List_DefaultsToNewestFirst()
    {
        var page = await ListAsync("?pageSize=100");

        page.Items.Select(u => u.CreatedAt).Should().BeInDescendingOrder();
    }

    [Test]
    public async Task List_SortsByEmailAscending_WhenAsked()
    {
        var page = await ListAsync("?sortBy=Email&isDescending=false&pageSize=100");

        page.Items.Select(u => u.Email).Should().BeInAscendingOrder();
    }

    [Test]
    public async Task List_RejectsAnOversizedPage()
    {
        var response = await Client.GetAsync($"{Endpoint}?pageSize=1000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "each page costs a second batch query for its roles");
    }

    [Test]
    public async Task List_RejectsAnInvertedDateRange()
    {
        var response = await Client.GetAsync(
            $"{Endpoint}?createdFrom={DateTime.UtcNow:O}&createdTo={DateTime.UtcNow.AddDays(-1):O}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task List_OmittingEveryOptionalParameter_StillBinds()
    {
        (await Client.GetAsync(Endpoint)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Detail, roles (#2, #15)

    [Test]
    public async Task Detail_ReturnsTheUser()
    {
        var userId = await AdminUserIdAsync();

        var response = await Client.GetAsync($"{Endpoint}/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<AdminUserDetailsResponse>();
        user!.Email.Should().Be(TestUsers.Admin.Email);
        user.Roles.Should().Contain(TestUsers.Admin.Role);
        user.LastLoginAt.Should().NotBeNull("SetUp logs this user in");
    }

    [Test]
    public async Task Detail_FindsASoftDeletedUser()
    {
        // The reason the detail read does not go through UserManager.FindByIdAsync: that runs under
        // the !IsDeleted filter and answers null for exactly the users an admin opens this card to
        // inspect.
        var email = $"s6-detail-{Guid.NewGuid():N}@test.com";
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        await UserManagementHelper.SoftDeleteUserAsync(Factory.Services, userId);

        var response = await Client.GetAsync($"{Endpoint}/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<AdminUserDetailsResponse>();
        user!.IsDeleted.Should().BeTrue();
        user.DeletedAt.Should().NotBeNull();
    }

    [Test]
    public async Task Detail_OfAnUnknownUser_Is404()
    {
        var response = await Client.GetAsync($"{Endpoint}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Roles_ReturnsTheUsersRoles()
    {
        var userId = await AdminUserIdAsync();

        var roles = await Client.GetFromJsonAsync<List<string>>($"{Endpoint}/{userId}/roles");

        roles.Should().Contain(TestUsers.Admin.Role);
    }

    [Test]
    public async Task Roles_OfAnUnknownUser_Is404()
    {
        var response = await Client.GetAsync($"{Endpoint}/{Guid.NewGuid()}/roles");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Sessions (#18) — the security-shaped half

    [Test]
    public async Task Sessions_ListTheUsersRefreshTokens()
    {
        var userId = await AdminUserIdAsync();

        var sessions = await Client.GetFromJsonAsync<List<AdminUserSessionResponse>>($"{Endpoint}/{userId}/sessions");

        sessions.Should().NotBeEmpty("SetUp logged this user in, which issues a refresh token");
        sessions!.Should().Contain(s => s.IsActive);
        sessions.Should().OnlyContain(s => s.ExpiresAt > s.CreatedAt);
    }

    [Test]
    public async Task SessionsResponse_ContainsNoTokenMaterial()
    {
        // The one assertion this endpoint exists to satisfy. The raw refresh token has not been
        // stored since SEC-04, but the table still holds a SHA-256 of it and a hash of whatever
        // superseded it on rotation — both credential-equivalent to an offline attacker. Asserted
        // against the RAW JSON rather than a deserialized DTO, because a DTO that gained the field
        // would still deserialize fine into a type that ignores it.
        var userId = await AdminUserIdAsync();

        var body = await Client.GetStringAsync($"{Endpoint}/{userId}/sessions");

        body.Should().NotBeEmpty();
        foreach (var forbidden in new[] { "tokenHash", "TokenHash", "replacedByToken", "ReplacedByToken", "token" })
        {
            body.Should().NotContain(forbidden,
                $"a session response must never carry '{forbidden}' — see AdminUserSessionDto");
        }
    }

    [Test]
    public async Task SessionsResponse_DoesNotContainTheStoredHashValue()
    {
        // Belt and braces for the field-name check above: read the actual hash out of the database
        // and assert the response does not contain it, which survives any renaming of the column.
        var userId = await AdminUserIdAsync();
        var storedHashes = await UserManagementHelper.GetRefreshTokenHashesAsync(Factory.Services, userId);
        storedHashes.Should().NotBeEmpty("precondition: this user has at least one refresh token");

        var body = await Client.GetStringAsync($"{Endpoint}/{userId}/sessions");

        foreach (var hash in storedHashes)
            body.Should().NotContain(hash);
    }

    [Test]
    public async Task Sessions_OfAUserWhoNeverSignedIn_IsAnEmptyList_Not404()
    {
        // An empty list is a meaningful answer and must not double as "no such user".
        var email = $"s6-nosession-{Guid.NewGuid():N}@test.com";
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        var response = await Client.GetAsync($"{Endpoint}/{userId}/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<AdminUserSessionResponse>>()).Should().BeEmpty();
    }

    [Test]
    public async Task Sessions_OfAnUnknownUser_Is404()
    {
        var response = await Client.GetAsync($"{Endpoint}/{Guid.NewGuid()}/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Stats (#19)

    [Test]
    public async Task StatsRoute_IsNotSwallowedByTheUserIdRoute()
    {
        // The user id is a STRING, not a Guid, so there is no route constraint to separate these —
        // only ASP.NET's rule that a literal segment outranks a parameter one. If that ever stopped
        // holding, "stats" would bind as {id} and this would answer 404 from the detail action.
        var response = await Client.GetAsync($"{Endpoint}/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AdminUserStatsResponse>()).Should().NotBeNull();
    }

    [Test]
    public async Task Stats_CountsLiveUsers_AndDeletedSeparately()
    {
        var before = await Client.GetFromJsonAsync<AdminUserStatsResponse>($"{Endpoint}/stats");

        var email = $"s6-stats-{Guid.NewGuid():N}@test.com";
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        var afterCreate = await Client.GetFromJsonAsync<AdminUserStatsResponse>($"{Endpoint}/stats");
        afterCreate!.Total.Should().Be(before!.Total + 1);

        await UserManagementHelper.SoftDeleteUserAsync(Factory.Services, userId);

        var afterDelete = await Client.GetFromJsonAsync<AdminUserStatsResponse>($"{Endpoint}/stats");
        afterDelete!.Total.Should().Be(before.Total, "a deleted user leaves the live total");
        afterDelete.Deleted.Should().Be(before.Deleted + 1,
            "and is counted separately — which needs IgnoreQueryFilters, or this would be 0 forever");
    }

    [Test]
    public async Task Stats_PeriodBoundsOnlyNewInPeriod()
    {
        // The tile reads "N total, M new in the period". Applying the window to the other counts
        // would make every figure mean something different from its label.
        var stats = await Client.GetFromJsonAsync<AdminUserStatsResponse>(
            $"{Endpoint}/stats?from={DateTime.UtcNow.AddYears(10):O}&to={DateTime.UtcNow.AddYears(11):O}");

        stats!.NewInPeriod.Should().Be(0, "no user was created ten years from now");
        stats.Total.Should().BeGreaterThan(0, "but the total is not bounded by the period");
    }

    [Test]
    public async Task Stats_RejectsAnInvertedRange()
    {
        var response = await Client.GetAsync(
            $"{Endpoint}/stats?from={DateTime.UtcNow:O}&to={DateTime.UtcNow.AddDays(-1):O}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}
