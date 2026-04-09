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
