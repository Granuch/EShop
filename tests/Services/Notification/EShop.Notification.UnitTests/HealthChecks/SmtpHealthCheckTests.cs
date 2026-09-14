using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.HealthChecks;
using EShop.Notification.UnitTests.TestDoubles;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EShop.Notification.UnitTests.HealthChecks;

/// <summary>
/// Notification audit S6 (M11, D9). The check connects and says EHLO, and never logs in: it used to authenticate on
/// every readiness probe. <see cref="FakeSmtpServer"/> advertises no AUTH, so a check that tried to log in would fail.
/// </summary>
[TestFixture]
public class SmtpHealthCheckTests
{
    [Test]
    public async Task AServerThatAcceptsConnections_IsHealthy_AndTheCheckNeverLogsIn()
    {
        await using var server = FakeSmtpServer.Start();

        var result = await Check(server.Port);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy), result.Exception?.Message);
            Assert.That(server.Commands, Has.Some.StartsWith("EHLO"), "precondition: the check reached the server");
            Assert.That(server.Commands, Has.None.StartsWith("AUTH"), "the credentials are configured but not used");
        });
    }

    [Test]
    public async Task NothingListening_IsUnhealthy()
    {
        var result = await Check(FakeSmtpServer.UnusedPort());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    private static Task<HealthCheckResult> Check(int port)
        => new SmtpHealthCheck(Options.Create(new SmtpSettings
            {
                Host = "127.0.0.1",
                Port = port,
                Security = SmtpSecurity.None,
                Username = "mailer",
                Password = "s3cret-smtp-password",
                CheckCertificateRevocation = false
            }))
            .CheckHealthAsync(new HealthCheckContext());
}
