using System.Text.Json;

namespace EShop.Basket.IntegrationTests.Configuration;

[TestFixture]
public class AppSettingsTests
{
    [Test]
    public void AppSettingsJson_ShouldContainBasketRedisSection()
    {
        var repoRoot = FindRepositoryRoot();
        var appSettingsPath = Path.Combine(repoRoot, "src", "Services", "Basket", "EShop.Basket.API", "appsettings.json");

        Assert.That(File.Exists(appSettingsPath), Is.True, $"appsettings.json not found at: {appSettingsPath}");

        var json = File.ReadAllText(appSettingsPath);
        using var document = JsonDocument.Parse(json);

        Assert.That(document.RootElement.TryGetProperty("BasketRedis", out _), Is.True);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "EShop.slnx");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing EShop.slnx.");
    }
}
