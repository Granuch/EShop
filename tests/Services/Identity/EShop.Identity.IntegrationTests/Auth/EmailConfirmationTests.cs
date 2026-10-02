using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.IntegrationTests.Fixtures;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using EShop.Identity.Domain.Entities;

namespace EShop.Identity.IntegrationTests.Auth;

/// <summary>
/// Email confirmation end to end, on real Postgres, on a host that requires it to sign in — strict mode, which is
/// off by default since soft email verification (that default is <see cref="SoftEmailVerificationTests"/>).
///
/// <para>
/// This replaces the startup rail that refused to boot with <c>RequireConfirmedEmail</c> on while
/// registration discarded its token: the token now travels on
/// <see cref="EmailConfirmationRequestedIntegrationEvent"/>, and these tests take it from the outbox
/// row — exactly what Notification will put in the email — and redeem it through the API. That is
/// the property the rail existed to protect: a newly registered account can always become able to
/// sign in.
/// </para>
///
/// <para>
/// The fixture shares one host and database, so every test creates its own user under a unique
/// address and diffs the outbox rather than counting it.
/// </para>
/// </summary>
[TestFixture]
public class EmailConfirmationTests : IntegrationTestBase
{
    private const string Password = "Confirm@123456";

    protected override async Task<IdentityApiFactory> CreateFactoryAsync()
        => await ConfirmedEmailRequiredApiFactory.CreateAsync();

    private static string UniqueEmail(string prefix) => $"{prefix}_{Guid.NewGuid():N}@test.com";

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

    private static List<OutboxMessage> ConfirmationRequests(IEnumerable<OutboxMessage> rows)
        => rows.Where(m => m.Type == typeof(EmailConfirmationRequestedIntegrationEvent).FullName).ToList();

    private static EmailConfirmationRequestedIntegrationEvent Payload(OutboxMessage row)
    {
        row.Payload.Should().NotContain("redacted", "the payload is only redacted once the row goes terminal");
        return JsonSerializer.Deserialize<EmailConfirmationRequestedIntegrationEvent>(
            row.Payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password)
        => Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });

    private Task<HttpResponseMessage> ResendAsync(string email)
        => Client.PostAsJsonAsync("/api/v1/auth/resend-confirmation", new { email });

    private Task<HttpResponseMessage> ConfirmAsync(string userId, string token)
        => Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token });

    private static async Task<string?> ErrorCodeOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    private async Task<bool> IsConfirmedAsync(string userId)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByIdAsync(userId))!.EmailConfirmed;
    }

    [Test]
    public void TheHost_RequiresConfirmedEmail()
    {
        // Guards the fixture itself: if the setting stopped arriving, every test below would run
        // against a host that lets unconfirmed users in, and the 403 tests would fail for a reason
        // that reads like a handler bug.
        var options = Factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.SignIn.RequireConfirmedEmail.Should().BeTrue();
    }

    [Test]
    public async Task Registration_SendsATokenThatUnlocksSignIn()
    {
        var email = UniqueEmail("reg");
        HttpResponseMessage? register = null;

        var added = await RowsAddedByAsync(async () =>
            register = await Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest
            {
                Email = email,
                Password = Password,
                FirstName = "Ada",
                LastName = "Lovelace"
            }));

        register!.StatusCode.Should().Be(HttpStatusCode.OK, await register.Content.ReadAsStringAsync());
        var userId = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.UserId;
        added.Should().ContainSingle(m => m.Type == typeof(UserRegisteredIntegrationEvent).FullName);
        var request = Payload(ConfirmationRequests(added).Should().ContainSingle().Subject);
        request.UserId.Should().Be(userId);

        var refused = await LoginAsync(email, Password);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeOf(refused)).Should().Be("Auth.EmailNotConfirmed");

        var confirm = await ConfirmAsync(userId, request.ConfirmationToken);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        var login = await LoginAsync(email, Password);
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task AWrongPassword_OnAnUnconfirmedAccount_IsTheUniform401()
    {
        // The unconfirmed state is named only to someone who knows the password; otherwise it
        // would tell a stranger which addresses are registered.
        var email = UniqueEmail("wrongpw");
        await UserManagementHelper.CreateTestUserAsync(Factory.Services, email, Password, emailConfirmed: false);

        var response = await LoginAsync(email, "Wrong@123456");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ErrorCodeOf(response)).Should().Be("Auth.InvalidCredentials");
    }

    /// <summary>
    /// The right password on an unconfirmed account is not a failed attempt. If it were, the
    /// brute-force tracker would throttle the third refusal onwards (<c>Auth.TooManyAttempts</c>) and,
    /// at five, lock the account — punishing a user for logging in before clicking their link.
    /// </summary>
    [Test]
    public async Task RepeatedLogins_WithTheRightPassword_BeforeConfirming_AreNeverThrottled()
    {
        var email = UniqueEmail("repeat");
        await UserManagementHelper.CreateTestUserAsync(Factory.Services, email, Password, emailConfirmed: false);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var response = await LoginAsync(email, Password);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"attempt {attempt}");
            (await ErrorCodeOf(response)).Should().Be("Auth.EmailNotConfirmed", $"attempt {attempt}");
        }
    }

    [Test]
    public async Task Resend_SendsAFreshTokenThatWorks()
    {
        var email = UniqueEmail("resend");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email, Password, emailConfirmed: false);
        HttpResponseMessage? resend = null;

        var added = await RowsAddedByAsync(async () => resend = await ResendAsync(email));

        resend!.StatusCode.Should().Be(HttpStatusCode.OK, await resend.Content.ReadAsStringAsync());
        var request = Payload(ConfirmationRequests(added).Should().ContainSingle().Subject);
        request.UserId.Should().Be(userId);

        (await ConfirmAsync(userId, request.ConfirmationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(email, Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Resend_ForAnUnknownOrConfirmedAddress_SendsNothing_AndAnswersExactlyLikeARealSend()
    {
        var pending = UniqueEmail("pending");
        await UserManagementHelper.CreateTestUserAsync(Factory.Services, pending, Password, emailConfirmed: false);
        var confirmed = UniqueEmail("confirmed");
        await UserManagementHelper.CreateTestUserAsync(Factory.Services, confirmed, Password, emailConfirmed: true);

        var real = await (await ResendAsync(pending)).Content.ReadAsStringAsync();

        string? unknownBody = null, confirmedBody = null;
        var added = await RowsAddedByAsync(async () =>
        {
            var unknown = await ResendAsync(UniqueEmail("nobody"));
            unknown.StatusCode.Should().Be(HttpStatusCode.OK);
            unknownBody = await unknown.Content.ReadAsStringAsync();

            var already = await ResendAsync(confirmed);
            already.StatusCode.Should().Be(HttpStatusCode.OK);
            confirmedBody = await already.Content.ReadAsStringAsync();
        });

        ConfirmationRequests(added).Should().BeEmpty();
        unknownBody.Should().Be(real, "an unknown address must be indistinguishable from a real send");
        confirmedBody.Should().Be(real, "so must an address that has already been confirmed");
    }

    [Test]
    public async Task Resend_Twice_InsideTheCooldown_SendsOnce()
    {
        var email = UniqueEmail("cooldown");
        await UserManagementHelper.CreateTestUserAsync(Factory.Services, email, Password, emailConfirmed: false);

        var added = await RowsAddedByAsync(async () =>
        {
            (await ResendAsync(email)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await ResendAsync(email)).StatusCode.Should().Be(HttpStatusCode.OK);
        });

        ConfirmationRequests(added).Should().ContainSingle(
            "one resend per account per minute, however often it is asked for");
    }

    [Test]
    public async Task Resend_WithAMalformedAddress_Is400()
    {
        var response = await ResendAsync("not-an-email");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(response)).Should().Be("ValidationError");
    }

    [Test]
    public async Task APasswordReset_ConfirmsTheAddress_SoAnInviteeCanSignIn()
    {
        // The invite shape: an unconfirmed account whose only email is a reset link.
        var email = UniqueEmail("invitee");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email, Password, emailConfirmed: false);

        var added = await RowsAddedByAsync(async () =>
            (await Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest { Email = email }))
                .StatusCode.Should().Be(HttpStatusCode.OK));
        var resetRow = added.Should()
            .ContainSingle(m => m.Type == typeof(PasswordResetRequestedIntegrationEvent).FullName).Subject;
        var resetToken = JsonSerializer.Deserialize<PasswordResetRequestedIntegrationEvent>(
            resetRow.Payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.ResetToken;

        var reset = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            userId,
            token = resetToken,
            newPassword = "Invited@123456"
        });
        reset.StatusCode.Should().Be(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());

        (await IsConfirmedAsync(userId)).Should().BeTrue();
        (await LoginAsync(email, "Invited@123456")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task AFailedPasswordReset_DoesNotConfirmTheAddress()
    {
        // TransactionBehavior commits a failure Result, so this is the case that proves the flag is
        // set only after the token was accepted.
        var email = UniqueEmail("badreset");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email, Password, emailConfirmed: false);

        var reset = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            userId,
            token = "not-a-real-token",
            newPassword = "Invited@123456"
        });

        reset.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IsConfirmedAsync(userId)).Should().BeFalse();
    }
}
