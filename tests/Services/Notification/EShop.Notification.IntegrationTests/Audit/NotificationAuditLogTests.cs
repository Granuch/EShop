using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Notification.Application.Notifications.Commands.MarkNotificationUndeliverable;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.IntegrationTests.Fixtures;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Notification.IntegrationTests.Audit;

/// <summary>
/// Admin panel S15 — Notification's operator actions are audited and served on <c>/api/v1/admin/audit</c>, under
/// <c>audit.read</c> rather than either of the notification permissions.
/// </summary>
[TestFixture]
[Category("Integration")]
public class NotificationAuditLogTests
{
    private NotificationApiFactory _factory = null!;
    private HttpClient _admin = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _factory = new NotificationApiFactory();
        _admin = _factory.CreateAdminClient();
    }

    [OneTimeTearDown]
    public void Stop()
    {
        _admin.Dispose();
        _factory.Dispose();
    }

    private async Task<Guid> SeedFailedAsync()
    {
        var log = NotificationLog.CreatePending(
            Guid.NewGuid(), "PaymentRefundedEvent", "corr-s15", "user-1", "seeded", "seeded subject", payload: null);
        log.BeginAttempt(DateTime.UtcNow);
        log.MarkFailed("Simulated SMTP failure");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync();
        return log.Id;
    }

    [Test]
    public async Task AnOperatorClosingANotification_WritesExactlyOneRow()
    {
        var id = await SeedFailedAsync();

        var response = await _admin.PostAsJsonAsync($"/api/v1/notifications/{id}/mark-undeliverable", new { reason = "bounced" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var page = await _admin.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/v1/admin/audit?entityType=Notification&entityId={id}");

        var row = page!.Items.Should().ContainSingle().Subject;
        row.Service.Should().Be("notification");
        row.Action.Should().Be("MarkNotificationUndeliverable");
        row.Outcome.Should().Be("Succeeded");
        row.ActorUserId.Should().Be("admin-1");
        row.PayloadJson.Should().Contain("bounced");
    }

    [Test]
    public void AuditRunsOutermost()
    {
        using var scope = _factory.Services.CreateScope();

        var order = scope.ServiceProvider
            .GetServices<IPipelineBehavior<MarkNotificationUndeliverableCommand, Result<NotificationDetailDto>>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ToList();

        order.IndexOf(typeof(AuditBehavior<,>)).Should().Be(0);
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Test]
    public async Task TheAuditTrail_NeedsAuditRead_NotANotificationPermission()
    {
        using var journalOperator = ClientWith(NotificationApiFactory.PermissionOnlyToken(EShopPermissions.NotificationsManage));
        using var auditor = ClientWith(NotificationApiFactory.PermissionOnlyToken(EShopPermissions.AuditRead));
        using var customer = _factory.CreateUserClient();
        using var anonymous = _factory.CreateClient();

        (await journalOperator.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await auditor.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
