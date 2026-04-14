using EShop.Notification.Infrastructure.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;

namespace EShop.Notification.UnitTests.Services;

[TestFixture]
public class TemplateRendererTests
{
    [Test]
    public async Task RenderAsync_ShouldReuseCachedTemplateContent()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"template-tests-{Guid.NewGuid():N}");
        var templatesPath = Path.Combine(contentRoot, "src", "Services", "Notification", "EShop.Notification.Infrastructure", "Templates");
        Directory.CreateDirectory(templatesPath);

        var templateFile = Path.Combine(templatesPath, "test-template.html");
        await File.WriteAllTextAsync(templateFile, "Hello {{Name}}");

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(x => x.ContentRootPath).Returns(contentRoot);
        env.SetupGet(x => x.EnvironmentName).Returns("Testing");
        env.SetupGet(x => x.ApplicationName).Returns("Test");
        env.SetupGet(x => x.ContentRootFileProvider).Returns(new NullFileProvider());

        var renderer = new TemplateRenderer(env.Object);

        var first = await renderer.RenderAsync("test-template", new Dictionary<string, string>
        {
            ["Name"] = "Alice"
        });

        await File.WriteAllTextAsync(templateFile, "Changed {{Name}}");

        var second = await renderer.RenderAsync("test-template", new Dictionary<string, string>
        {
            ["Name"] = "Bob"
        });

        Assert.That(first, Is.EqualTo("Hello Alice"));
        Assert.That(second, Is.EqualTo("Hello Bob"));

        Directory.Delete(contentRoot, recursive: true);
    }
}
