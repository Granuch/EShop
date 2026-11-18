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
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// The storefront links the emails carry, each shipped on the same terms: no tracked default, a local one for
    /// Development, compose and k8s, and a required one in the production override. Email confirmation joined the
    /// password reset in email-confirmation Stage 3.
    /// </summary>
    private static readonly object[] StorefrontUrls =
    [
        new object[] { "PasswordReset", "ResetUrlBase", "PASSWORD_RESET_URL_BASE", "http://localhost:3000/reset-password" },
        new object[] { "EmailConfirmation", "ConfirmUrlBase", "EMAIL_CONFIRMATION_URL_BASE", "http://localhost:3000/confirm-email" }
    ];

    [TestCaseSource(nameof(StorefrontUrls))]
    public void TheTrackedAppsettings_SetNoStorefrontUrl_ButDevelopmentDoes(
        string section, string property, string envVar, string localUrl)
    {
        var tracked = ReadJson("appsettings.json");
        var development = ReadJson("appsettings.Development.json");

        Assert.Multiple(() =>
        {
            Assert.That(tracked.TryGetProperty(section, out _), Is.False,
                "a tracked default reached every deployed environment unnoticed (M9)");
            Assert.That(development.GetProperty(section).GetProperty(property).GetString(), Is.EqualTo(localUrl));
        });
    }

    [TestCaseSource(nameof(StorefrontUrls))]
    public void Compose_GivesNotificationApiTheUrl_OverridableFromTheEnvironment(
        string section, string property, string envVar, string localUrl)
    {
        var key = $"{section}__{property}";

        Assert.That(ComposeServiceBlock("docker-compose.yml", "notification-api")
                .Where(line => line.StartsWith($"{key}:", StringComparison.Ordinal)),
            Is.EqualTo(new[] { $"{key}: ${{{envVar}:-{localUrl}}}" }));
    }

    /// <summary>Production refuses the local default, so its override must not fall back to it.</summary>
    [TestCaseSource(nameof(StorefrontUrls))]
    public void TheProductionOverride_RequiresTheUrl(string section, string property, string envVar, string localUrl)
    {
        var key = $"{section}__{property}";

        Assert.That(ComposeServiceBlock("docker-compose.override.production.yml", "notification-api")
                .Where(line => line.StartsWith($"{key}:", StringComparison.Ordinal)),
            Is.EqualTo(new[] { $"{key}: ${{{envVar}:?{envVar} is required in production}}" }));
    }

    [TestCaseSource(nameof(StorefrontUrls))]
    public void EnvExample_DeclaresTheUrl(string section, string property, string envVar, string localUrl)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot, ".env.example"));

        Assert.That(lines.Count(line => line == $"{envVar}={localUrl}"), Is.EqualTo(1));
    }

    [TestCaseSource(nameof(StorefrontUrls))]
    public void Kubernetes_GivesNotificationApiTheUrl(string section, string property, string envVar, string localUrl)
    {
        // notification-config in deploy/'s base (a configMapGenerator literal); the server overlay merges its own URL.
        var key = $"- {section}__{property}=";

        Assert.That(
            KustomizationLines().Where(line => line.StartsWith(key, StringComparison.Ordinal)),
            Is.EqualTo(new[] { key + localUrl }));
    }

    /// <summary>S5 (D7): every shipped configuration names the SMTP security mode, and none still sets UseSsl.</summary>
    [Test]
    public void EveryShippedConfiguration_NamesTheSmtpSecurityMode_NotUseSsl()
    {
        var compose = ComposeServiceBlock("docker-compose.yml", "notification-api");
        var production = ComposeServiceBlock("docker-compose.override.production.yml", "notification-api");
        // configMapGenerator literals in deploy/'s base, "- Key=value"; compared without the "- ".
        var configMap = KustomizationLines().Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..]).ToList();
        var tracked = ReadJson("appsettings.json").GetProperty("Smtp");
        var development = ReadJson("appsettings.Development.json").GetProperty("Smtp");

        Assert.Multiple(() =>
        {
            Assert.That(compose, Does.Contain("Smtp__Security: ${NOTIFICATION_SMTP_SECURITY:-None}"));
            Assert.That(production,
                Does.Contain("Smtp__Security: ${NOTIFICATION_SMTP_SECURITY:?NOTIFICATION_SMTP_SECURITY is required in production}"));
            Assert.That(configMap, Does.Contain("Smtp__Security=None"));
            Assert.That(tracked.GetProperty("Security").GetString(), Is.EqualTo("StartTls"));
            Assert.That(development.GetProperty("Security").GetString(), Is.EqualTo("StartTls"));

            Assert.That(compose.Concat(production).Concat(configMap), Has.None.StartsWith("Smtp__UseSsl"));
            Assert.That(tracked.TryGetProperty("UseSsl", out _), Is.False);
            Assert.That(development.TryGetProperty("UseSsl", out _), Is.False);
        });
    }

    /// <summary>S5 (D7): the Mailpit stack needs no credentials, and placeholder ones are refused outside Development.</summary>
    [Test]
    public void EnvExample_ShipsNoSmtpCredentials_AndThePlaintextModeForMailpit()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot, ".env.example"));

        Assert.Multiple(() =>
        {
            Assert.That(lines, Does.Contain("NOTIFICATION_SMTP_USERNAME="));
            Assert.That(lines, Does.Contain("NOTIFICATION_SMTP_PASSWORD="));
            Assert.That(lines, Does.Contain("NOTIFICATION_SMTP_SECURITY=None"));
            Assert.That(lines, Has.None.StartsWith("NOTIFICATION_SMTP_USE_SSL"));
        });
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

    /// <summary>The uncommented, trimmed lines of deploy/base/apps/kustomization.yaml.</summary>
    private static List<string> KustomizationLines()
        => File.ReadAllLines(Path.Combine(RepositoryRoot, "deploy", "base", "apps", "kustomization.yaml"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

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
