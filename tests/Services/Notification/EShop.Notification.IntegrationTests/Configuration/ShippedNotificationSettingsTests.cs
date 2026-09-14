using System.Text.Json;
using System.Text.RegularExpressions;

namespace EShop.Notification.IntegrationTests.Configuration;

/// <summary>
/// Notification audit S4 (M9, D4): what the repository ships. No test host reads the compose files, the k8s manifest or
/// <c>.env.example</c>, and a missing reset URL now stops the host, so only a test that reads the files notices one of
/// them losing it. Each check is scoped to notification-api's own block.
/// </summary>
[TestFixture]
public class ShippedNotificationSettingsTests
{
    private const string LocalResetUrl = "http://localhost:3000/reset-password";
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Test]
    public void TheTrackedAppsettings_SetNoResetUrl_ButDevelopmentDoes()
    {
        var tracked = ReadJson("appsettings.json");
        var development = ReadJson("appsettings.Development.json");

        Assert.Multiple(() =>
        {
            Assert.That(tracked.TryGetProperty("PasswordReset", out _), Is.False,
                "a tracked default reached every deployed environment unnoticed (M9)");
            Assert.That(development.GetProperty("PasswordReset").GetProperty("ResetUrlBase").GetString(),
                Is.EqualTo(LocalResetUrl));
        });
    }

    [Test]
    public void Compose_GivesNotificationApiAResetUrl_OverridableFromTheEnvironment()
    {
        Assert.That(ComposeServiceBlock("docker-compose.yml", "notification-api")
                .Where(line => line.StartsWith("PasswordReset__ResetUrlBase:", StringComparison.Ordinal)),
            Is.EqualTo(new[] { $"PasswordReset__ResetUrlBase: ${{PASSWORD_RESET_URL_BASE:-{LocalResetUrl}}}" }));
    }

    /// <summary>Production refuses the local default, so its override must not fall back to it.</summary>
    [Test]
    public void TheProductionOverride_RequiresTheResetUrl()
    {
        Assert.That(ComposeServiceBlock("docker-compose.override.production.yml", "notification-api")
                .Where(line => line.StartsWith("PasswordReset__ResetUrlBase:", StringComparison.Ordinal)),
            Is.EqualTo(new[]
            {
                "PasswordReset__ResetUrlBase: ${PASSWORD_RESET_URL_BASE:?PASSWORD_RESET_URL_BASE is required in production}"
            }));
    }

    [Test]
    public void EnvExample_DeclaresTheResetUrl()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot, ".env.example"));

        Assert.That(lines.Count(line => line == $"PASSWORD_RESET_URL_BASE={LocalResetUrl}"), Is.EqualTo(1));
    }

    [Test]
    public void Kubernetes_GivesNotificationApiAResetUrl()
    {
        var deployment = KubernetesDeployment("notification-api");
        var index = deployment.FindIndex(line => line == "- name: PasswordReset__ResetUrlBase");

        Assert.That(index, Is.GreaterThanOrEqualTo(0), "notification-api's Deployment sets no PasswordReset__ResetUrlBase");
        Assert.That(deployment.Count(line => line == "- name: PasswordReset__ResetUrlBase"), Is.EqualTo(1));
        Assert.That(deployment[index + 1], Is.EqualTo($"value: \"{LocalResetUrl}\""));
    }

    private static JsonElement ReadJson(string file)
        => JsonDocument.Parse(
                File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Services", "Notification", "EShop.Notification.API", file)),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            .RootElement;

    /// <summary>The uncommented, trimmed lines of one service in a compose file, up to the next service.</summary>
    private static List<string> ComposeServiceBlock(string file, string service)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot, file));
        var start = Array.IndexOf(lines, $"  {service}:");
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{file} has no {service} service");

        var nextService = new Regex(@"^(  )?[A-Za-z0-9_-]+:\s*$");
        return lines
            .Skip(start + 1)
            .TakeWhile(line => !nextService.IsMatch(line))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
    }

    /// <summary>The uncommented, trimmed lines of the Deployment named <paramref name="name"/> in the k8s manifest.</summary>
    private static List<string> KubernetesDeployment(string name)
    {
        var documents = File.ReadAllText(Path.Combine(RepositoryRoot, "k8s", "services", "microservices.yaml"))
            .ReplaceLineEndings("\n")
            .Split("\n---\n")
            .Select(document => document.Split('\n').Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#')).ToList())
            .Where(document => document.Contains("kind: Deployment") && document.Contains($"name: {name}"))
            .ToList();

        Assert.That(documents, Has.Count.EqualTo(1), $"expected one Deployment named {name}");
        return documents[0];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EShop.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("EShop.slnx not found above the test directory.");
    }
}
