using System.Text.RegularExpressions;

namespace EShop.BuildingBlocks.UnitTests.Messaging;

/// <summary>
/// frontend-contracts R7 (F-56). <c>RabbitMQ:UseDelayedExchangePlugin</c> makes <c>AddEShopBus</c> configure MassTransit's
/// delayed redelivery, which declares an <c>x-delayed-message</c> exchange. The broker every shipped configuration runs,
/// the stock <c>rabbitmq:3.13-management-alpine</c> image, has no <c>rabbitmq_delayed_message_exchange</c> plugin, so the
/// broker closed the channel with <c>406 PRECONDITION_FAILED</c>, the message was requeued, and a failing delivery was
/// retried forever instead of reaching its <c>_error</c> queue. Compose set the flag for notification-api and the k8s
/// ConfigMap for every service. No test host reads these files, so only a test that reads them can notice the flag
/// coming back.
/// </summary>
[TestFixture]
public class ShippedBrokerSettingsTests
{
    private const string Flag = "UseDelayedExchangePlugin";
    private const string Plugin = "rabbitmq_delayed_message_exchange";

    private static readonly string[] ShippedFiles =
    [
        "docker-compose.yml",
        "docker-compose.override.production.yml",
        "docker-compose.override.public.yml",
        "k8s/02-configmap.yaml"
    ];

    [Test]
    public void NoShippedConfiguration_EnablesTheDelayedExchangePlugin()
    {
        var enabled = ShippedFiles
            .SelectMany(file => SettingLines(file).Select(line => $"{file}: {line}"))
            .Where(line => !Regex.IsMatch(line, $@"{Flag}\s*:\s*""?false""?\s*$", RegexOptions.IgnoreCase))
            .ToList();

        Assert.That(enabled, Is.Empty,
            "the shipped broker has no delayed-message plugin, so enabling it makes a failing message retry forever (F-56)");
    }

    [Test]
    public void TheShippedFiles_StillSayWhatTheySay()
    {
        // The two places that used to enable it now disable it explicitly, so a reader of either file sees the decision
        // rather than a missing line; this also keeps the test above from passing on files it no longer finds.
        Assert.Multiple(() =>
        {
            Assert.That(SettingLines("docker-compose.yml"), Is.EqualTo(new[] { $"RabbitMQ__{Flag}: \"false\"" }));
            Assert.That(SettingLines("k8s/02-configmap.yaml"), Is.EqualTo(new[] { $"RabbitMQ__{Flag}: \"false\"" }));
        });
    }

    [Test]
    public void TheShippedBroker_StillLacksThePlugin()
    {
        // Paired with the tests above, so the rule cannot outlive its reason: install the plugin (a custom image, an
        // enabled_plugins file) and this tells you the flag may be turned back on, deliberately, with it.
        var mentions = new[] { "docker-compose.yml", "docker-compose.override.production.yml", "docker-compose.override.public.yml" }
            .Concat(Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "k8s"), "*.yaml", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(RepositoryRoot(), path)))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "infrastructure"), "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(RepositoryRoot(), path)))
            .Where(file => File.ReadAllText(Path.Combine(RepositoryRoot(), file)).Contains(Plugin, StringComparison.Ordinal))
            .ToList();

        Assert.That(mentions, Is.Empty,
            "a shipped file now mentions the delayed-message plugin; if the broker really has it, the flag may be enabled "
            + "again and these tests updated with it");
    }

    /// <summary>Every uncommented line of <paramref name="file"/> naming the flag, trimmed.</summary>
    private static List<string> SettingLines(string file)
        => File.ReadAllLines(Path.Combine(RepositoryRoot(), file))
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#') && line.Contains(Flag, StringComparison.Ordinal))
            .ToList();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EShop.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("EShop.slnx not found above the test directory.");
    }
}
