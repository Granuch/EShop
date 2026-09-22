using System.Net;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Notification.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Notification.IntegrationTests.Security;

/// <summary>
/// Admin panel S12. Notification's first authorization surface, guarded the way Catalog's and Payment's are: a
/// structural check that every endpoint under <c>/api/</c> carries the policy this file names, and behavioural checks
/// that an anonymous and a signed-in non-admin caller are actually refused.
///
/// <para>
/// Each half catches what the other cannot. Downgrading one endpoint to <c>RequireAuthorization()</c> turns the
/// structural check red while every anonymous 401 stays green — a valid non-admin token is the only thing that tells
/// them apart, and a suite signing in only as Admin never would. Conversely, redefining the permission policy as
/// <c>RequireAuthenticatedUser()</c> leaves the structural check green and turns the 403s red.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class NotificationAuthorizationTests
{
    /// <summary>
    /// Every route this service serves under <c>/api/</c>, with the policy it must carry. A route added or removed
    /// without updating this table fails <see cref="EveryApiEndpoint_IsListedHere_WithItsPolicy"/>.
    /// </summary>
    private static readonly Dictionary<string, string> ExpectedPolicies = new(StringComparer.Ordinal)
    {
        ["GET /api/v1/notifications"] = EShopPermissions.NotificationsRead,
        ["GET /api/v1/notifications/stats"] = EShopPermissions.NotificationsRead,
        ["GET /api/v1/notifications/{id:guid}"] = EShopPermissions.NotificationsRead,

        // Admin panel S13. The template list is a read; every action — the test send included, which writes nothing but
        // emails an address the caller picks — takes the manage permission.
        ["GET /api/v1/notifications/templates"] = EShopPermissions.NotificationsRead,
        // Admin panel S15: this service's slice of the audit trail; the gateway serves the merged view.
        ["GET /api/v1/admin/audit"] = EShopPermissions.AuditRead,
        ["POST /api/v1/notifications/templates/{name}/test"] = EShopPermissions.NotificationsManage,
        ["POST /api/v1/notifications/retry-failed"] = EShopPermissions.NotificationsManage,
        ["POST /api/v1/notifications/{id:guid}/resend"] = EShopPermissions.NotificationsManage,
        ["POST /api/v1/notifications/{id:guid}/mark-undeliverable"] = EShopPermissions.NotificationsManage
    };

    private static readonly string[] JournalPaths =
    [
        "/api/v1/notifications",
        "/api/v1/notifications/stats",
        "/api/v1/notifications/11111111-1111-1111-1111-111111111111",
        "/api/v1/notifications/templates"
    ];

    /// <summary>Admin panel S13: every action, each with a body its handler would accept.</summary>
    private static readonly (string Path, object? Body)[] ActionRequests =
    [
        ("/api/v1/notifications/11111111-1111-1111-1111-111111111111/resend", null),
        ("/api/v1/notifications/retry-failed", new { limit = 1 }),
        ("/api/v1/notifications/11111111-1111-1111-1111-111111111111/mark-undeliverable", new { reason = "x" }),
        ("/api/v1/notifications/templates/order-created/test", new { email = "ops@eshop.test" })
    ];

    private NotificationApiFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _factory = new NotificationApiFactory();

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    // ---------- structural ----------

    [Test]
    public void EveryApiEndpoint_IsListedHere_WithItsPolicy()
    {
        var actual = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(
                e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
                     ?? ["*"],
                (e, method) => (
                    Key: $"{method} {e.RoutePattern.RawText!.TrimEnd('/')}",
                    Policy: e.Metadata.GetOrderedMetadata<IAuthorizeData>().LastOrDefault()?.Policy))
            .ToDictionary(x => x.Key, x => x.Policy, StringComparer.Ordinal);

        Assert.That(actual.Keys, Is.EquivalentTo(ExpectedPolicies.Keys),
            "a /api/ endpoint was added or removed — list it above with the policy it must carry. Notification has no "
            + "\"Admin\" policy to fall back on, so an endpoint that forgets one is simply authenticated-only.");

        foreach (var (key, expected) in ExpectedPolicies)
        {
            Assert.That(actual[key], Is.EqualTo(expected), $"'{key}' carries the wrong policy");
        }
    }

    [Test]
    public void TheHealthMetricsAndRootEndpoints_StayAnonymous()
    {
        // Adding authentication to a service must not lock its probes out: a readiness endpoint behind a bearer token
        // makes every Kubernetes probe fail, and the failure looks like the service being unhealthy.
        var guarded = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) != true)
            .Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0)
            .Select(e => e.RoutePattern.RawText)
            .ToArray();

        Assert.That(guarded, Is.Empty);
    }

    // ---------- behavioural ----------

    [Test]
    public async Task TheJournal_RefusesAnonymous_WithUnauthorized()
    {
        // A second client from the factory, not a nulled Authorization header: HttpClient merges DefaultRequestHeaders
        // into any request that does not already carry them, so nulling the header on the request leaves the token in
        // place and the test passes for the wrong reason.
        using var anonymous = _factory.CreateClient();

        foreach (var path in JournalPaths)
        {
            Assert.That((await anonymous.GetAsync(path)).StatusCode,
                Is.EqualTo(HttpStatusCode.Unauthorized), path);
        }
    }

    [Test]
    public async Task TheJournal_RefusesASignedInNonAdmin_WithForbidden()
    {
        using var user = _factory.CreateUserClient();

        foreach (var path in JournalPaths)
        {
            Assert.That((await user.GetAsync(path)).StatusCode,
                Is.EqualTo(HttpStatusCode.Forbidden), path);
        }
    }

    [Test]
    public async Task TheJournal_AdmitsAnAdmin_ThroughTheRoleBundle()
    {
        // The Admin role carries no "permission" claim; RolePermissionBundles is what turns it into notifications.read.
        // Without that, declaring a permission here would have locked every existing administrator out.
        using var admin = _factory.CreateAdminClient();

        Assert.That((await admin.GetAsync("/api/v1/notifications")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task TheJournal_AdmitsAPermissionClaim_WithNoRoleAtAll()
    {
        // The other half of Q4c: the handler reads the claim directly, so granting one operator the journal later is
        // an Identity-only change and touches no endpoint here.
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", NotificationApiFactory.PermissionOnlyToken(EShopPermissions.NotificationsRead));

        Assert.That((await client.GetAsync("/api/v1/notifications")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ADifferentPermission_DoesNotOpenTheJournal()
    {
        // Control for the test above: it must be notifications.read that admits, not "any permission claim at all".
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", NotificationApiFactory.PermissionOnlyToken(EShopPermissions.NotificationsManage));

        Assert.That((await client.GetAsync("/api/v1/notifications")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // ---------- Admin panel S13: the actions ----------

    [Test]
    public async Task TheActions_RefuseAnonymous_WithUnauthorized()
    {
        using var anonymous = _factory.CreateClient();

        foreach (var (path, body) in ActionRequests)
        {
            Assert.That((await Post(anonymous, path, body)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), path);
        }
    }

    [Test]
    public async Task TheActions_RefuseASignedInNonAdmin_WithForbidden()
    {
        using var user = _factory.CreateUserClient();

        foreach (var (path, body) in ActionRequests)
        {
            Assert.That((await Post(user, path, body)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), path);
        }
    }

    /// <summary>
    /// The one that tells the two permissions apart. An action declared with <c>notifications.read</c> by mistake would
    /// pass every admin test and refuse every customer exactly as before; only a caller who may read but not manage
    /// sees the difference — the support operator the permission split exists for.
    /// </summary>
    [Test]
    public async Task TheReadPermission_DoesNotOpenTheActions()
    {
        using var reader = _factory.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", NotificationApiFactory.PermissionOnlyToken(EShopPermissions.NotificationsRead));

        foreach (var (path, body) in ActionRequests)
        {
            Assert.That((await Post(reader, path, body)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), path);
        }
    }

    [Test]
    public async Task TheManagePermission_OpensTheActions_WithNoRoleAtAll()
    {
        using var manager = _factory.CreateClient();
        manager.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", NotificationApiFactory.PermissionOnlyToken(EShopPermissions.NotificationsManage));

        // Past authorization, into the handler: the notification does not exist.
        Assert.That((await Post(manager, ActionRequests[0].Path, null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object? body)
        => body is null
            ? client.PostAsync(path, null)
            : client.PostAsync(path, System.Net.Http.Json.JsonContent.Create(body));
}
