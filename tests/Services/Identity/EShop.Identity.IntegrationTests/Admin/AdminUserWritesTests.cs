using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// Admin panel S7 — the write half of the admin user screens: create, edit, email, activate,
/// deactivate, delete, restore, lock, unlock, reset password, confirm email, disable 2FA, set roles
/// and revoke sessions (endpoints #3–#14, #16, #17).
/// </summary>
/// <remarks>
/// <para>
/// Almost every test here asserts an <b>effect</b> rather than a status code, because for most of
/// these the status code is the same whether the implementation works or not. "Deactivate returned
/// 204" is true of a handler that changed nothing; "the user can no longer sign in" is not.
/// </para>
/// <para>
/// The fixture shares one host and one database with its neighbours (PERF-02), so every test
/// creates its own user under a unique email rather than touching a seeded one. The seeded admin is
/// the caller and must stay usable — a test that locked or deleted it would break every test after
/// it.
/// </para>
/// </remarks>
[TestFixture]
[Category("Integration")]
public class AdminUserWritesTests : AuthenticatedIntegrationTestBase
{
    private const string Endpoint = "/api/v1/admin/users";
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string DefaultPassword = "Test@123456";

    private static string UniqueEmail(string tag) => $"s7-{tag}-{Guid.NewGuid():N}@test.com";

    private Task<string> ArrangeUserAsync(string tag, string role = TestUsers.Roles.User)
        => UserManagementHelper.CreateTestUserAsync(Factory.Services, UniqueEmail(tag), role: role);

    private async Task<AdminUserDetailsResponse> DetailAsync(string userId)
    {
        var response = await Client.GetAsync($"{Endpoint}/{userId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AdminUserDetailsResponse>())!;
    }

    private async Task<List<string>> RolesOfAsync(string userId)
        => (await Client.GetFromJsonAsync<List<string>>($"{Endpoint}/{userId}/roles"))!;

    /// <summary>
    /// Signs in on a client that carries no admin bearer token. A second client rather than nulling
    /// the header: <c>HttpClient</c> merges <c>DefaultRequestHeaders</c> into any request that lacks
    /// them, so nulling a request's header leaves the admin token in place.
    /// </summary>
    private async Task<HttpResponseMessage> AttemptLoginAsync(string email, string password)
    {
        using var anonymous = Factory.CreateClient();
        return await anonymous.PostAsJsonAsync(LoginEndpoint, new LoginRequest { Email = email, Password = password });
    }

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var response = await AttemptLoginAsync(email, password);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    /// <summary>
    /// The access token's role claims, not the login response's <c>user.roles</c>. Only one of the
    /// two goes through <c>ICachedUserRolesService</c>, and it is this one — asserting on the body
    /// cannot see a roles-cache bug at all.
    /// </summary>
    private static IEnumerable<string> RolesInToken(string accessToken) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .Where(c => c.Type is ClaimTypes.Role or "role")
            .Select(c => c.Value);

    private async Task<List<OutboxMessage>> ReadOutboxAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>Only the rows the action itself added — the fixture shares a database.</summary>
    private async Task<List<OutboxMessage>> RowsAddedByAsync(Func<Task> action)
    {
        var before = (await ReadOutboxAsync()).Select(m => m.Id).ToHashSet();
        await action();
        return (await ReadOutboxAsync()).Where(m => !before.Contains(m.Id)).ToList();
    }

    private static string ResetTokenFrom(OutboxMessage row)
    {
        var payload = JsonSerializer.Deserialize<PasswordResetRequestedIntegrationEvent>(
            row.Payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        payload.ResetToken.Should().NotBeNullOrEmpty();
        return payload.ResetToken;
    }

    #region Create (#3)

    [Test]
    public async Task Create_WithAPassword_MakesAnAccountThatCanSignInImmediately()
    {
        var email = UniqueEmail("create");

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            email,
            firstName = "Ada",
            lastName = "Lovelace",
            password = "Created@123456",
            emailConfirmed = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.Location.Should().NotBeNull("201 must point at the detail card it just made");

        var created = await response.Content.ReadFromJsonAsync<CreateUserResult>();
        created!.InviteSent.Should().BeFalse();

        var login = await LoginAsync(email, "Created@123456");
        login.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Create_WithoutAPassword_SendsAnInvite_AndTheEmailedTokenWorks()
    {
        // The Q2a shape: an admin never chooses a password, so an invited account is created with
        // none and the reset link is what makes it usable. Asserting the token actually resets the
        // password is the difference between "we published an event" and "the invite works".
        var email = UniqueEmail("invite");
        HttpResponseMessage? response = null;

        var added = await RowsAddedByAsync(async () =>
            response = await Client.PostAsJsonAsync(Endpoint, new
            {
                email,
                firstName = "Grace",
                lastName = "Hopper",
                emailConfirmed = true
            }));

        response!.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<CreateUserResult>())!;
        created.InviteSent.Should().BeTrue();

        var row = added.Should()
            .ContainSingle(m => m.Type == typeof(PasswordResetRequestedIntegrationEvent).FullName).Subject;

        (await AttemptLoginAsync(email, "Anything@123456")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "the account has no password yet");

        var reset = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            userId = created.UserId,
            token = ResetTokenFrom(row),
            newPassword = "Invited@123456"
        });
        reset.StatusCode.Should().Be(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());

        (await LoginAsync(email, "Invited@123456")).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Create_WithATakenEmail_Is409()
    {
        var email = UniqueEmail("dup");
        await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            email,
            firstName = "Ada",
            lastName = "Lovelace",
            password = "Created@123456"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Create_WithADeletedAccountsEmail_Is409_NotA500()
    {
        // ASP.NET Identity's UserNameIndex is NOT filtered on IsDeleted, unlike Catalog's SKU index
        // (risk A3) — so a deleted account keeps occupying its address. FindByEmailAsync runs under
        // the !IsDeleted query filter and would report it free, and the insert would then fail at
        // the database as an unhandled DbUpdateException, because Identity registers no DbUpdate*
        // ProblemDetails branch. The pre-check lifts the filter for exactly this reason.
        var email = UniqueEmail("recycled");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        await UserManagementHelper.SoftDeleteUserAsync(Factory.Services, userId);

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            email,
            firstName = "Ada",
            lastName = "Lovelace",
            password = "Created@123456"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Create_WithAnUnknownRole_Is404_AndCreatesNothing()
    {
        var email = UniqueEmail("badrole");

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            email,
            firstName = "Ada",
            lastName = "Lovelace",
            password = "Created@123456",
            roles = new[] { "User", "Wizard" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The point of pre-checking: a role validated after CreateAsync would leave the account
        // behind, because TransactionBehavior commits on a Result failure.
        var page = await Client.GetFromJsonAsync<AdminUserPage>($"{Endpoint}?search={email}&pageSize=10");
        page!.Items.Should().BeEmpty("no account may survive a refused create");
    }

    [Test]
    public async Task Create_WithoutRoles_GrantsTheDefaultUserRole()
    {
        var email = UniqueEmail("defaultrole");

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            email,
            firstName = "Ada",
            lastName = "Lovelace",
            password = "Created@123456"
        });

        var created = (await response.Content.ReadFromJsonAsync<CreateUserResult>())!;
        (await RolesOfAsync(created.UserId)).Should().BeEquivalentTo([TestUsers.Roles.User],
            "an omitted list means 'the same roles self-registration would give'");
    }

    [Test]
    public async Task Create_WithAnExplicitlyEmptyRoleList_GrantsNone()
    {
        var email = UniqueEmail("noroles");

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            email,
            firstName = "Ada",
            lastName = "Lovelace",
            password = "Created@123456",
            roles = Array.Empty<string>()
        });

        var created = (await response.Content.ReadFromJsonAsync<CreateUserResult>())!;
        (await RolesOfAsync(created.UserId)).Should().BeEmpty(
            "omitted and empty are different requests — that is the BUG-09 rule applied to a collection");
    }

    [Test]
    public async Task Create_WithAWeakPassword_Is400_AndCreatesNothing()
    {
        var email = UniqueEmail("weak");

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            email,
            firstName = "Ada",
            lastName = "Lovelace",
            password = "abc"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var page = await Client.GetFromJsonAsync<AdminUserPage>($"{Endpoint}?search={email}&pageSize=10");
        page!.Items.Should().BeEmpty();
    }

    #endregion

    #region Update profile (#4)

    [Test]
    public async Task Update_WithOneField_LeavesTheOthersAlone()
    {
        var userId = await ArrangeUserAsync("update");
        await Client.PutAsJsonAsync($"{Endpoint}/{userId}", new
        {
            firstName = "Original",
            lastName = "Person",
            phoneNumber = "+15550000",
            profilePictureUrl = "https://cdn.test/avatar.png"
        });

        var response = await Client.PutAsJsonAsync($"{Endpoint}/{userId}", new { firstName = "Renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await DetailAsync(userId);
        detail.FirstName.Should().Be("Renamed");
        detail.LastName.Should().Be("Person", "an omitted field arrives as null and must not clear anything");
        detail.PhoneNumber.Should().Be("+15550000");
        detail.ProfilePictureUrl.Should().Be("https://cdn.test/avatar.png");
    }

    [Test]
    public async Task Update_WithAnEmptyString_ClearsTheFieldDeliberately()
    {
        var userId = await ArrangeUserAsync("clear");
        await Client.PutAsJsonAsync($"{Endpoint}/{userId}", new { phoneNumber = "+15550000" });

        await Client.PutAsJsonAsync($"{Endpoint}/{userId}", new { phoneNumber = "" });

        (await DetailAsync(userId)).PhoneNumber.Should().BeNull(
            "omitting means 'leave it', so there has to be some way to actually remove a value");
    }

    [Test]
    public async Task Update_WithABlankName_Is400()
    {
        var userId = await ArrangeUserAsync("blankname");

        var response = await Client.PutAsJsonAsync($"{Endpoint}/{userId}", new { firstName = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a name may be omitted but not blanked — no other path can produce a user without one");
    }

    [Test]
    public async Task Update_EvictsTheUsersOwnCachedProfile()
    {
        // GET /api/v1/account/profile caches profile:{userId} for five minutes. Without the
        // ICacheInvalidatingCommand marker the admin's edit is invisible to the user for that long,
        // and nothing fails — the read just keeps answering from the cache.
        var email = UniqueEmail("profilecache");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        var login = await LoginAsync(email, DefaultPassword);
        using var asUser = Factory.CreateClient();
        asUser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var primed = await asUser.GetFromJsonAsync<JsonElement>("/api/v1/account/profile");
        primed.GetProperty("firstName").GetString().Should().Be("Test");

        await Client.PutAsJsonAsync($"{Endpoint}/{userId}", new { firstName = "Reassigned" });

        var after = await asUser.GetFromJsonAsync<JsonElement>("/api/v1/account/profile");
        after.GetProperty("firstName").GetString().Should().Be("Reassigned",
            "profile:{id} is written by CachingBehavior, so the marker can and must evict it");
    }

    #endregion

    #region Email (#5)

    [Test]
    public async Task ChangeEmail_MovesTheUserNameTooSoTheUserCanStillSignIn()
    {
        var email = UniqueEmail("email");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        var newEmail = UniqueEmail("email-new");

        var response = await Client.PutAsJsonAsync($"{Endpoint}/{userId}/email",
            new { email = newEmail, markConfirmed = true });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await DetailAsync(userId);
        detail.Email.Should().Be(newEmail);
        detail.UserName.Should().Be(newEmail,
            "login looks the account up by email while the unique index is on the user name");
        detail.EmailConfirmed.Should().BeTrue();

        (await LoginAsync(newEmail, DefaultPassword)).AccessToken.Should().NotBeNullOrEmpty();
        (await AttemptLoginAsync(email, DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ChangeEmail_WithoutMarkConfirmed_LeavesTheAddressUnconfirmed()
    {
        var userId = await ArrangeUserAsync("unconfirm");
        (await DetailAsync(userId)).EmailConfirmed.Should().BeTrue("precondition: the seed confirms it");

        await Client.PutAsJsonAsync($"{Endpoint}/{userId}/email", new { email = UniqueEmail("unconfirm-new") });

        (await DetailAsync(userId)).EmailConfirmed.Should().BeFalse(
            "nobody has verified the new address, and the safe default is to say so");
    }

    [Test]
    public async Task ChangeEmail_ToATakenAddress_Is409_AndChangesNothing()
    {
        var takenEmail = UniqueEmail("occupied");
        await UserManagementHelper.CreateTestUserAsync(Factory.Services, takenEmail);

        var email = UniqueEmail("mover");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        var response = await Client.PutAsJsonAsync($"{Endpoint}/{userId}/email", new { email = takenEmail });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The assertion the status code cannot make. UserManager.UpdateAsync validates before it
        // saves, so relying on it to refuse the duplicate leaves a mutated tracked entity that
        // TransactionBehavior then commits — a 409 that changed the email anyway.
        var detail = await DetailAsync(userId);
        detail.Email.Should().Be(email);
        detail.UserName.Should().Be(email);
        (await LoginAsync(email, DefaultPassword)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ChangeEmail_ToItsOwnAddress_IsAllowed()
    {
        var email = UniqueEmail("selfsame");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        var response = await Client.PutAsJsonAsync($"{Endpoint}/{userId}/email",
            new { email, markConfirmed = true });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "without excluding the user being edited, their own row answers 'taken'");
    }

    #endregion

    #region Activate, deactivate (#6, #7)

    [Test]
    public async Task Deactivate_StopsTheUserSigningIn_AndRevokesTheirSessions()
    {
        var email = UniqueEmail("deactivate");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        var login = await LoginAsync(email, DefaultPassword);

        var response = await Client.PostAsync($"{Endpoint}/{userId}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DetailAsync(userId)).IsActive.Should().BeFalse();
        (await AttemptLoginAsync(email, DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var sessions = await Client.GetFromJsonAsync<List<AdminUserSessionResponse>>($"{Endpoint}/{userId}/sessions");
        sessions!.Should().NotBeEmpty("precondition: the login above issued one");
        sessions.Should().OnlyContain(s => !s.IsActive,
            "a suspended account's refresh tokens must not be waiting to be reused on re-activation");
    }

    [Test]
    public async Task Activate_LetsADeactivatedUserSignInAgain()
    {
        var email = UniqueEmail("reactivate");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        await Client.PostAsync($"{Endpoint}/{userId}/deactivate", null);

        var response = await Client.PostAsync($"{Endpoint}/{userId}/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoginAsync(email, DefaultPassword)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Activate_OnADeletedAccount_Is404()
    {
        var userId = await ArrangeUserAsync("activatedeleted");
        await UserManagementHelper.SoftDeleteUserAsync(Factory.Services, userId);

        var response = await Client.PostAsync($"{Endpoint}/{userId}/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the write paths keep treating a deleted account as absent; restore it first");
    }

    #endregion

    #region Delete, restore (#8, #9)

    [Test]
    public async Task Delete_HidesTheAccount_AndStopsItSigningIn()
    {
        var email = UniqueEmail("delete");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        var response = await Client.DeleteAsync($"{Endpoint}/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var live = await Client.GetFromJsonAsync<AdminUserPage>($"{Endpoint}?search={email}&pageSize=10");
        live!.Items.Should().BeEmpty();

        var detail = await DetailAsync(userId);
        detail.IsDeleted.Should().BeTrue();
        detail.DeletedAt.Should().NotBeNull();
        detail.IsActive.Should().BeFalse("SoftDelete moves all three fields together");

        (await AttemptLoginAsync(email, DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Restore_BringsTheAccountBackDeactivated()
    {
        // Decision Q1a, and the reason this test asserts a login FAILURE after a successful restore.
        var email = UniqueEmail("restore");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        await Client.DeleteAsync($"{Endpoint}/{userId}");

        var response = await Client.PostAsync($"{Endpoint}/{userId}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await DetailAsync(userId);
        detail.IsDeleted.Should().BeFalse();
        detail.DeletedAt.Should().BeNull();
        detail.IsActive.Should().BeFalse("restoring and re-enabling are two decisions");

        (await AttemptLoginAsync(email, DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Restore_ThenActivate_FullyRevivesTheAccount()
    {
        var email = UniqueEmail("revive");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        await Client.DeleteAsync($"{Endpoint}/{userId}");

        await Client.PostAsync($"{Endpoint}/{userId}/restore", null);
        var activate = await Client.PostAsync($"{Endpoint}/{userId}/activate", null);

        activate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoginAsync(email, DefaultPassword)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Restore_ReappearsInTheLiveList_AndLeavesTheDeletedOne()
    {
        var email = UniqueEmail("restorelist");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        await Client.DeleteAsync($"{Endpoint}/{userId}");

        await Client.PostAsync($"{Endpoint}/{userId}/restore", null);

        var live = await Client.GetFromJsonAsync<AdminUserPage>($"{Endpoint}?search={email}&pageSize=10");
        live!.Items.Should().ContainSingle(u => u.Id == userId);

        var deleted = await Client.GetFromJsonAsync<AdminUserPage>($"{Endpoint}?search={email}&isDeleted=true&pageSize=10");
        deleted!.Items.Should().BeEmpty();
    }

    [Test]
    public async Task Restore_OfAnAccountThatIsNotDeleted_Is409()
    {
        var userId = await ArrangeUserAsync("notdeleted");

        var response = await Client.PostAsync($"{Endpoint}/{userId}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region Lock, unlock (#10, #11)

    [Test]
    public async Task Lock_PreventsSignIn_AndShowsOnTheDetailCard()
    {
        var email = UniqueEmail("lock");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        var until = DateTimeOffset.UtcNow.AddHours(2);

        var response = await Client.PostAsJsonAsync($"{Endpoint}/{userId}/lock",
            new { until, reason = "Suspicious activity" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await DetailAsync(userId);
        detail.IsLockedOut.Should().BeTrue();
        detail.LockoutEnd.Should().BeCloseTo(until, TimeSpan.FromSeconds(5));

        (await AttemptLoginAsync(email, DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Lock_WithAPastMoment_Is400_AndChangesNothing()
    {
        var email = UniqueEmail("pastlock");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        var response = await Client.PostAsJsonAsync($"{Endpoint}/{userId}/lock",
            new { until = DateTimeOffset.UtcNow.AddHours(-1) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await DetailAsync(userId)).IsLockedOut.Should().BeFalse();
        (await LoginAsync(email, DefaultPassword)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Unlock_LiftsTheLockout_AndClearsTheFailureCount()
    {
        var email = UniqueEmail("unlock");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        await Client.PostAsJsonAsync($"{Endpoint}/{userId}/lock", new { until = DateTimeOffset.UtcNow.AddHours(2) });
        await UserManagementHelper.SetAccessFailedCountAsync(Factory.Services, userId, 4);

        var response = await Client.PostAsync($"{Endpoint}/{userId}/unlock", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await DetailAsync(userId);
        detail.IsLockedOut.Should().BeFalse();
        detail.LockoutEnd.Should().BeNull();
        detail.AccessFailedCount.Should().Be(0);
        (await UserManagementHelper.GetAccessFailedCountAsync(Factory.Services, userId)).Should().Be(0);

        (await LoginAsync(email, DefaultPassword)).AccessToken.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Reset password (#12)

    [Test]
    public async Task ResetPassword_EmailsATokenThatReplacesTheOldPassword()
    {
        // Q2a turned this endpoint into "send a link", so the effect worth asserting is the whole
        // chain: outbox row -> token -> the old password stops working. A status-code-only test
        // passes against a handler that enqueues nothing.
        var email = UniqueEmail("reset");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        HttpResponseMessage? response = null;

        var added = await RowsAddedByAsync(async () =>
            response = await Client.PostAsync($"{Endpoint}/{userId}/reset-password", null));

        response!.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var row = added.Should()
            .ContainSingle(m => m.Type == typeof(PasswordResetRequestedIntegrationEvent).FullName).Subject;
        row.Payload.Should().NotContain("redacted", "the payload is only redacted once the row goes terminal");

        var reset = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            userId,
            token = ResetTokenFrom(row),
            newPassword = "Rotated@123456"
        });
        reset.StatusCode.Should().Be(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());

        (await AttemptLoginAsync(email, DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the old password must stop working once the emailed link is used");
        (await LoginAsync(email, "Rotated@123456")).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ResetPassword_LeavesTheOldPasswordWorkingUntilTheLinkIsUsed()
    {
        // The other half of Q2a, stated so nobody "fixes" it: asking for a reset does not itself
        // invalidate anything. An admin who clicks this by mistake has not locked anyone out.
        var email = UniqueEmail("resetpending");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        await Client.PostAsync($"{Endpoint}/{userId}/reset-password", null);

        (await LoginAsync(email, DefaultPassword)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ResetPassword_ForAnUnknownUser_Is404()
    {
        // Deliberately unlike POST /auth/forgot-password, which answers success for a missing
        // address to stop anonymous enumeration. The caller here can already list every user.
        var response = await Client.PostAsync($"{Endpoint}/{Guid.NewGuid()}/reset-password", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Confirm email, disable 2FA (#13, #14)

    [Test]
    public async Task ConfirmEmail_MarksTheAddressConfirmed_AndPublishesTheSameEventAsSelfService()
    {
        var email = UniqueEmail("confirm");
        var userId = await UserManagementHelper.CreateTestUserAsync(
            Factory.Services, email, emailConfirmed: false);
        HttpResponseMessage? response = null;

        var added = await RowsAddedByAsync(async () =>
            response = await Client.PostAsync($"{Endpoint}/{userId}/confirm-email", null));

        response!.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DetailAsync(userId)).EmailConfirmed.Should().BeTrue();
        added.Should().ContainSingle(m => m.Type == typeof(UserEmailConfirmedIntegrationEvent).FullName);
    }

    [Test]
    public async Task ConfirmEmail_OnAnAlreadyConfirmedAddress_IsANoOp()
    {
        var userId = await ArrangeUserAsync("reconfirm");

        var added = await RowsAddedByAsync(() => Client.PostAsync($"{Endpoint}/{userId}/confirm-email", null));

        added.Should().BeEmpty("a panel button clicked twice must not publish twice");
    }

    [Test]
    public async Task DisableTwoFactor_TurnsItOff_WithoutAskingForACode()
    {
        // The support path for a lost authenticator: the self-service handler needs a valid TOTP
        // code, which is precisely what the user no longer has.
        var userId = await ArrangeUserAsync("2fa");
        await UserManagementHelper.SetTwoFactorEnabledAsync(Factory.Services, userId, true);
        (await DetailAsync(userId)).TwoFactorEnabled.Should().BeTrue("precondition");

        var response = await Client.PostAsync($"{Endpoint}/{userId}/disable-2fa", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DetailAsync(userId)).TwoFactorEnabled.Should().BeFalse();
    }

    [Test]
    public async Task DisableTwoFactor_WhenItIsNotEnabled_Is400()
    {
        var userId = await ArrangeUserAsync("no2fa");

        var response = await Client.PostAsync($"{Endpoint}/{userId}/disable-2fa", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Roles (#16)

    [Test]
    public async Task SetRoles_ReplacesTheWholeSet()
    {
        var userId = await ArrangeUserAsync("setroles");

        var response = await Client.PutAsJsonAsync($"{Endpoint}/{userId}/roles",
            new { roles = new[] { TestUsers.Roles.Admin } });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RolesOfAsync(userId)).Should().BeEquivalentTo([TestUsers.Roles.Admin],
            "this is a replacement, not a merge — the User role has to be gone");
    }

    [Test]
    public async Task SetRoles_WithAnEmptyList_StripsEveryRole()
    {
        var userId = await ArrangeUserAsync("striproles");

        var response = await Client.PutAsJsonAsync($"{Endpoint}/{userId}/roles", new { roles = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RolesOfAsync(userId)).Should().BeEmpty();
    }

    [Test]
    public async Task SetRoles_WithAnUnknownRole_Is404_AndLeavesTheOldSetIntact()
    {
        // The atomicity contract. The unknown name is second so a handler validating as it went
        // would already have granted Admin before noticing — and TransactionBehavior would commit
        // that grant while answering 404.
        var userId = await ArrangeUserAsync("partialroles");

        var response = await Client.PutAsJsonAsync($"{Endpoint}/{userId}/roles",
            new { roles = new[] { TestUsers.Roles.Admin, "Wizard" } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RolesOfAsync(userId)).Should().BeEquivalentTo([TestUsers.Roles.User],
            "a refused replacement must leave the user exactly as they were");
    }

    [Test]
    public async Task SetRoles_EvictsTheRolesCache_SoTheNextTokenCarriesTheNewRoles()
    {
        // SEC-02's shape. Minting a token warms user_roles:{id} for five minutes, and that cache is
        // NOT reachable through ICacheInvalidatingCommand — the handler has to call
        // CachedUserRolesService itself. Asserted on the decoded token, because the login response's
        // user.roles comes from UserManager directly and cannot see this bug.
        var email = UniqueEmail("rolecache");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);

        var before = await LoginAsync(email, DefaultPassword);
        RolesInToken(before.AccessToken).Should().NotContain(TestUsers.Roles.Admin);

        await Client.PutAsJsonAsync($"{Endpoint}/{userId}/roles", new { roles = new[] { TestUsers.Roles.Admin } });

        var afterGrant = await LoginAsync(email, DefaultPassword);
        RolesInToken(afterGrant.AccessToken).Should().Contain(TestUsers.Roles.Admin);

        // The direction that matters for security.
        await Client.PutAsJsonAsync($"{Endpoint}/{userId}/roles", new { roles = new[] { TestUsers.Roles.User } });

        var afterRevoke = await LoginAsync(email, DefaultPassword);
        RolesInToken(afterRevoke.AccessToken).Should().NotContain(TestUsers.Roles.Admin,
            "a demotion must not survive in a token minted after it");
    }

    #endregion

    #region Revoke sessions (#17)

    [Test]
    public async Task RevokeTokens_MakesTheOldRefreshTokenUnusable()
    {
        var email = UniqueEmail("revoke");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        var login = await LoginAsync(email, DefaultPassword);

        var response = await Client.PostAsync($"{Endpoint}/{userId}/revoke-tokens", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var anonymous = Factory.CreateClient();
        var refresh = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh-token",
            new { refreshToken = login.RefreshToken });

        // 401, not 400: AuthController maps Auth.* failures on this endpoint to Unauthorized, the
        // same answer a forged token gets. Asserted as observed rather than as expected — the
        // endpoint's contract predates this stage.
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a revoked refresh token must not still buy a new access token");
    }

    [Test]
    public async Task RevokeTokens_LeavesThePasswordWorking()
    {
        // The limit of what this endpoint claims: sessions end, the account is untouched.
        var email = UniqueEmail("revokelogin");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email);
        await LoginAsync(email, DefaultPassword);

        await Client.PostAsync($"{Endpoint}/{userId}/revoke-tokens", null);

        (await LoginAsync(email, DefaultPassword)).AccessToken.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Cross-cutting

    private static readonly string[][] UnknownUserWrites =
    [
        ["PUT", "{id}"],
        ["PUT", "{id}/email"],
        ["POST", "{id}/activate"],
        ["POST", "{id}/deactivate"],
        ["DELETE", "{id}"],
        ["POST", "{id}/restore"],
        ["POST", "{id}/lock"],
        ["POST", "{id}/unlock"],
        ["POST", "{id}/reset-password"],
        ["POST", "{id}/confirm-email"],
        ["POST", "{id}/disable-2fa"],
        ["PUT", "{id}/roles"],
        ["POST", "{id}/revoke-tokens"]
    ];

    [TestCaseSource(nameof(UnknownUserWrites))]
    public async Task EveryWrite_AgainstAnUnknownUser_Is404(string[] route)
    {
        var (method, template) = (route[0], route[1]);
        var path = $"{Endpoint}/{template.Replace("{id}", Guid.NewGuid().ToString())}";

        // Bodies that satisfy each endpoint's validator, so the 404 comes from the handler's
        // not-found rather than from a validation failure that would mask it.
        var body = template switch
        {
            "{id}" when method == "PUT" => (object)new { firstName = "Ada" },
            "{id}/email" => new { email = UniqueEmail("ghost") },
            "{id}/lock" => new { until = DateTimeOffset.UtcNow.AddHours(1) },
            "{id}/roles" => new { roles = new[] { TestUsers.Roles.User } },
            _ => new { }
        };

        var response = method switch
        {
            "PUT" => await Client.PutAsJsonAsync(path, body),
            "DELETE" => await Client.DeleteAsync(path),
            _ => await Client.PostAsJsonAsync(path, body)
        };

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"{method} {template}; body: {await response.Content.ReadAsStringAsync()}");
    }

    #endregion

    private sealed record CreateUserResult
    {
        public string UserId { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public bool InviteSent { get; init; }
    }
}
