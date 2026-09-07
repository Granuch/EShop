using System.Text;
using System.Text.Json;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.Identity.UnitTests.Infrastructure;

/// <summary>
/// SEC-07. All three health endpoints are anonymous in every component, and they used
/// <c>UIResponseWriter.WriteHealthCheckUIResponse</c>, which serialises each check's
/// description, its data dictionary and its exception message. On a failing service that
/// returned the database host, username and raw connection error to any caller — the internal
/// topology, handed out at precisely the moment the service was in trouble.
///
/// A probe needs to know which check failed, not why. These tests pin that the body carries
/// name and status only, whatever the checks put in their results.
/// </summary>
[TestFixture]
public class HealthResponseWriterTests
{
    private const string Secret =
        "Host=identity-postgres;Username=postgres;Password=hunter2 — connection refused";

    private static HealthReport ReportWith(HealthReportEntry entry, string name = "postgresql") =>
        new(
            new Dictionary<string, HealthReportEntry> { [name] = entry },
            TimeSpan.FromMilliseconds(12.34));

    private static async Task<string> WriteAsync(HealthReport report)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await EShopHealthResponseWriter.WriteAsync(context, report);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
    }

    [Test]
    public async Task Body_DoesNotCarryTheDescription_TheDataOrTheException()
    {
        var entry = new HealthReportEntry(
            HealthStatus.Unhealthy,
            description: Secret,
            duration: TimeSpan.FromMilliseconds(5),
            exception: new InvalidOperationException(Secret),
            data: new Dictionary<string, object> { ["error"] = Secret });

        var body = await WriteAsync(ReportWith(entry));

        Assert.That(body, Does.Not.Contain("identity-postgres"));
        Assert.That(body, Does.Not.Contain("hunter2"));
        Assert.That(body, Does.Not.Contain("connection refused"));
        Assert.That(body, Does.Not.Contain("error"));
    }

    [Test]
    public async Task Body_CarriesOverallStatusAndPerCheckNameAndStatus()
    {
        var entry = new HealthReportEntry(
            HealthStatus.Unhealthy,
            description: Secret,
            duration: TimeSpan.FromMilliseconds(5),
            exception: null,
            data: null);

        var body = await WriteAsync(ReportWith(entry));
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("Unhealthy"));

        var checks = root.GetProperty("checks");
        Assert.That(checks.GetArrayLength(), Is.EqualTo(1));
        Assert.That(checks[0].GetProperty("name").GetString(), Is.EqualTo("postgresql"));
        Assert.That(checks[0].GetProperty("status").GetString(), Is.EqualTo("Unhealthy"));

        // A probe that only reads the top-level status still works, and the operator can see
        // which check is the problem without being told anything about the infrastructure.
        Assert.That(root.TryGetProperty("totalDurationMs", out _), Is.True);
    }

    [Test]
    public async Task Body_IsCamelCase()
    {
        // Extension-style keys are easy to get wrong: the serializer's naming policy applies to
        // the properties here, but the existing HealthCheckTests assert on "Healthy" appearing
        // in the body, so the *values* stay PascalCase. Pin both.
        var entry = new HealthReportEntry(
            HealthStatus.Healthy,
            description: null,
            duration: TimeSpan.Zero,
            exception: null,
            data: null);

        var body = await WriteAsync(ReportWith(entry, "identity-liveness"));

        Assert.That(body, Does.Contain("\"totalDurationMs\""));
        Assert.That(body, Does.Not.Contain("\"TotalDurationMs\""));
        Assert.That(body, Does.Contain("Healthy"));
    }
}
