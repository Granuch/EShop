using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.Identity.Application.Users.Commands.DeactivateUser;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.IntegrationTests.Helpers;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// Admin panel S15 — Identity's admin commands are audited, and the audit is served on <c>/api/v1/admin/audit</c>.
/// Signs in as the seeded admin, so the actor recorded is a real account's id.
/// </summary>
[TestFixture]
[Category("Integration")]
public class AdminAuditLogTests : AuthenticatedIntegrationTestBase
{
    private const string Endpoint = "/api/v1/admin/users";

    private async Task<string> AdminIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Email == TestUsers.Admin.Email)).Id;
    }

    private async Task<AuditLogPageDto> AuditForUserAsync(string userId)
        => (await Client.GetFromJsonAsync<AuditLogPageDto>($"/api/v1/admin/audit?entityType=User&entityId={userId}"))!;

    [Test]
    public async Task AnAdminDeactivation_WritesExactlyOneRow_NamingTheUserAndTheAdmin()
    {
        var userId = await UserManagementHelper.CreateTestUserAsync(
            Factory.Services, $"s15-deactivate-{Guid.NewGuid():N}@test.com");

        (await Client.PostAsync($"{Endpoint}/{userId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = (await AuditForUserAsync(userId)).Items.Should().ContainSingle().Subject;
        row.Service.Should().Be("identity");
        row.Action.Should().Be("DeactivateUser");
        row.Outcome.Should().Be("Succeeded");
        row.ActorUserId.Should().Be(await AdminIdAsync());
    }

    [Test]
    public async Task ACreatedUser_IsNamedFromTheResult_AndTheirPasswordIsNeverStored()
    {
        const string password = "Audited@123456";
        var email = $"s15-create-{Guid.NewGuid():N}@test.com";

        using var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            email,
            firstName = "Ada",
            lastName = "Lovelace",
            password,
            emailConfirmed = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = (await db.Users.AsNoTracking().SingleAsync(u => u.Email == email)).Id;

        var row = (await AuditForUserAsync(userId)).Items.Should().ContainSingle().Subject;
        row.Action.Should().Be("CreateUser");
        // By value: whatever field it might have landed in, the password must not be in the table.
        row.PayloadJson.Should().NotContain(password);
        row.PayloadJson.Should().Contain(email, "the payload is otherwise the request, as the logs already show it");
    }

    [Test]
    public void AuditRunsOutermost()
    {
        using var scope = Factory.Services.CreateScope();

        var order = scope.ServiceProvider
            .GetServices<IPipelineBehavior<DeactivateUserCommand, Result<Unit>>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ToList();

        order.IndexOf(typeof(AuditBehavior<,>)).Should().Be(0,
            "outside TransactionBehavior, so a commit that fails is recorded as Failed rather than Succeeded");
    }
}

/// <summary>The audit endpoint requires <c>audit.read</c>; a signed-in regular user does not hold it.</summary>
[TestFixture]
[Category("Integration")]
public class AdminAuditLogAuthorizationTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserEmail => TestUsers.RegularUser.Email;
    protected override string TestUserPassword => TestUsers.RegularUser.Password;

    [Test]
    public async Task ARegularUser_IsForbidden()
        => (await Client.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Test]
    public async Task AnAnonymousCaller_IsUnauthorized()
    {
        using var anonymous = Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
