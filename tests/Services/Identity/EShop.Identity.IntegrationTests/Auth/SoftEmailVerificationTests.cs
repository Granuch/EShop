using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.Identity.IntegrationTests.Auth;

/// <summary>
/// Soft email verification end to end, on real Postgres, on the default host — where confirmation is not required to
/// sign in. An unconfirmed account signs in at once and its token says <c>email_verified=false</c>; confirming the
/// address and then refreshing is what makes it <c>true</c>, which is what Basket's checkout and Ordering's
/// <c>POST /orders</c> require. Strict mode (login refused until confirmed) stays covered by
/// <see cref="EmailConfirmationTests"/>.
///
/// <para>
/// The fixture shares one host and database, so every test registers its own user under a unique address and diffs
/// the outbox rather than counting it.
/// </para>
/// </summary>
[TestFixture]
public class SoftEmailVerificationTests : IntegrationTestBase
{
    private const string Password = "Soft@123456";

    private static string UniqueEmail(string prefix) => $"{prefix}_{Guid.NewGuid():N}@test.com";

    private async Task<List<OutboxMessage>> ReadOutboxAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    private async Task<List<OutboxMessage>> RowsAddedByAsync(Func<Task> action)
    {
        var before = (await ReadOutboxAsync()).Select(m => m.Id).ToHashSet();
        await action();
        return (await ReadOutboxAsync()).Where(m => !before.Contains(m.Id)).ToList();
    }

    private static EmailConfirmationRequestedIntegrationEvent ConfirmationRequest(IEnumerable<OutboxMessage> rows)
    {
        var row = rows.Should()
            .ContainSingle(m => m.Type == typeof(EmailConfirmationRequestedIntegrationEvent).FullName).Subject;
        return JsonSerializer.Deserialize<EmailConfirmationRequestedIntegrationEvent>(
            row.Payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private async Task<LoginResponse> LoginAsync(string email)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<RefreshTokenResponse> RefreshAsync(string refreshToken)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/refresh-token", new RefreshTokenRequest { RefreshToken = refreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RefreshTokenResponse>())!;
    }

    /// <summary>The claim's values in a raw access token — exactly what Basket and Ordering will read.</summary>
    private static List<string> EmailVerifiedClaims(string accessToken)
        => new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims
            .Where(c => c.Type == EmailVerification.ClaimType)
            .Select(c => c.Value)
            .ToList();

    [Test]
    public void TheHost_DoesNotRequireConfirmedEmail()
    {
        // Guards the fixture, and the shipped default: if strict mode came back, every login below would be a 403.
        Factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value
            .SignIn.RequireConfirmedEmail.Should().BeFalse();
    }

    [Test]
    public async Task ANewAccount_SignsInAtOnce_ThenConfirmingAndRefreshing_MakesItVerified()
    {
        var email = UniqueEmail("soft");
        HttpResponseMessage? register = null;
        var added = await RowsAddedByAsync(async () =>
            register = await Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest
            {
                Email = email,
                Password = Password,
                FirstName = "Grace",
                LastName = "Hopper"
            }));
        register!.StatusCode.Should().Be(HttpStatusCode.OK, await register.Content.ReadAsStringAsync());
        var userId = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.UserId;
        var request = ConfirmationRequest(added);

        // 1. Signed in before confirming: tokens issued, and both the body and the token say "unconfirmed".
        var login = await LoginAsync(email);
        login.AccessToken.Should().NotBeNullOrEmpty();
        login.User!.EmailConfirmed.Should().BeFalse();
        EmailVerifiedClaims(login.AccessToken).Should().Equal("false");

        // 2. The link is followed.
        var confirm = await Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token = request.ConfirmationToken });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        // 3. The access token in hand still says false — it is a signed snapshot — and a refresh re-reads the user.
        EmailVerifiedClaims(login.AccessToken).Should().Equal("false");
        var refreshed = await RefreshAsync(login.RefreshToken);
        EmailVerifiedClaims(refreshed.AccessToken).Should().Equal("true");

        // 4. A later login agrees.
        var again = await LoginAsync(email);
        again.User!.EmailConfirmed.Should().BeTrue();
        EmailVerifiedClaims(again.AccessToken).Should().Equal("true");
    }

    [Test]
    public async Task AConfirmedAccount_IsVerifiedFromItsFirstLogin()
    {
        var email = UniqueEmail("confirmed");
        await UserManagementHelper.CreateTestUserAsync(Factory.Services, email, Password, emailConfirmed: true);

        var login = await LoginAsync(email);

        login.User!.EmailConfirmed.Should().BeTrue();
        EmailVerifiedClaims(login.AccessToken).Should().Equal("true");
    }

    [Test]
    public async Task ASignedInUnconfirmedAccount_CanStillAskForANewLink()
    {
        var email = UniqueEmail("resend");
        var userId = await UserManagementHelper.CreateTestUserAsync(Factory.Services, email, Password, emailConfirmed: false);
        await LoginAsync(email);

        HttpResponseMessage? resend = null;
        var added = await RowsAddedByAsync(async () =>
            resend = await Client.PostAsJsonAsync("/api/v1/auth/resend-confirmation", new { email }));

        resend!.StatusCode.Should().Be(HttpStatusCode.OK);
        ConfirmationRequest(added).UserId.Should().Be(userId);
    }
}
