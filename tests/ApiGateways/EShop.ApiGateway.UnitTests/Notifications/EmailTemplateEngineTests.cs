using EShop.ApiGateway.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EShop.ApiGateway.UnitTests.Notifications;

[TestFixture]
public sealed class EmailTemplateEngineTests
{
    [Test]
    public void Render_UsesMappedTemplateFile_AndSubstitutesPlaceholders()
    {
        var root = CreateTempRoot();
        var templatesDir = Path.Combine(root, "Templates");
        Directory.CreateDirectory(templatesDir);

        File.WriteAllText(
            Path.Combine(templatesDir, "rate-limit.html"),
            "EVENT={{EventType}};ROUTE={{Route}};STATUS={{StatusCode}};USER={{UserId}}");

        var environment = BuildEnvironment(root);
        var engine = new EmailTemplateEngine(environment, NullLogger<EmailTemplateEngine>.Instance);

        var context = new EmailNotificationContext(
            EventType: "RateLimitExceeded",
            Route: "test-route",
            StatusCode: 429,
            Path: "/test",
            UserId: "user-1",
            UserEmail: null,
            CorrelationId: "corr-1",
            OccurredAtUtc: DateTime.UtcNow);

        var result = engine.Render(context);

        Assert.That(result.Subject, Does.Contain("RateLimitExceeded"));
        Assert.That(result.HtmlBody, Does.Contain("EVENT=RateLimitExceeded"));
        Assert.That(result.HtmlBody, Does.Contain("ROUTE=test-route"));
        Assert.That(result.HtmlBody, Does.Contain("STATUS=429"));
        Assert.That(result.HtmlBody, Does.Contain("USER=user-1"));
    }

    [Test]
    public void Render_UsesFallback_WhenTemplateFileMissing()
    {
        var root = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(root, "Templates"));

        var environment = BuildEnvironment(root);
        var engine = new EmailTemplateEngine(environment, NullLogger<EmailTemplateEngine>.Instance);

        var context = new EmailNotificationContext(
            EventType: "DownstreamFailure",
            Route: "proxy-route",
            StatusCode: 502,
            Path: "/test/proxy",
            UserId: "user-2",
            UserEmail: null,
            CorrelationId: "corr-2",
            OccurredAtUtc: DateTime.UtcNow);

        var result = engine.Render(context);

        Assert.That(result.HtmlBody, Does.Contain("Gateway event"));
        Assert.That(result.HtmlBody, Does.Contain("DownstreamFailure"));
        Assert.That(result.HtmlBody, Does.Contain("proxy-route"));
    }

    [Test]
    public void Render_ReportsTheNoticesHeldBack_InTheShippedTemplate()
    {
        var engine = new EmailTemplateEngine(BuildEnvironment(ShippedContentRoot()), NullLogger<EmailTemplateEngine>.Instance);
        var context = new EmailNotificationContext(
            EventType: "RateLimitExceeded",
            Route: "catalog-products-read-route",
            StatusCode: 429,
            Path: "/api/v1/products",
            UserId: null,
            UserEmail: null,
            CorrelationId: "corr-3",
            OccurredAtUtc: DateTime.UtcNow,
            SuppressedCount: 641);

        var result = engine.Render(context);

        Assert.That(result.HtmlBody, Does.Contain("held back since the previous one: 641"));
        Assert.That(result.HtmlBody, Does.Not.Contain("{{"));
    }

    /// <summary>A template without the placeholder would hide the size of a burst from the operator.</summary>
    [Test]
    public void EveryShippedTemplate_ShowsTheHeldBackCount()
    {
        var templates = Directory.GetFiles(Path.Combine(ShippedContentRoot(), "Templates"), "*.html");

        Assert.That(templates, Has.Length.EqualTo(5));
        Assert.That(
            templates.Where(t => !File.ReadAllText(t).Contains("{{SuppressedCount}}", StringComparison.Ordinal)).Select(Path.GetFileName),
            Is.Empty);
    }

    [Test]
    public void Render_Fallback_ReportsTheNoticesHeldBack()
    {
        var root = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(root, "Templates"));
        var engine = new EmailTemplateEngine(BuildEnvironment(root), NullLogger<EmailTemplateEngine>.Instance);
        var context = new EmailNotificationContext(
            "DownstreamFailure", "proxy-route", 502, "/x", null, null, "corr-4", DateTime.UtcNow, SuppressedCount: 12);

        Assert.That(engine.Render(context).HtmlBody, Does.Contain("held back since the previous one: 12"));
    }

    /// <summary>The gateway project's own folder, found by walking up from the test binaries to the solution file.</summary>
    private static string ShippedContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EShop.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "EShop.slnx not found above the test binaries");
        return Path.Combine(dir!.FullName, "src", "ApiGateways", "EShop.ApiGateway");
    }

    private static IWebHostEnvironment BuildEnvironment(string root)
    {
        var mock = new Mock<IWebHostEnvironment>();
        mock.SetupGet(x => x.ContentRootPath).Returns(root);
        return mock.Object;
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "eshop-gateway-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
