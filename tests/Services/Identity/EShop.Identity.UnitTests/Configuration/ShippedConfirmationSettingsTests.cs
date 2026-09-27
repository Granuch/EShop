namespace EShop.Identity.UnitTests.Configuration;

/// <summary>
/// Email-confirmation Stage 3: what the repository ships for <c>Identity:RequireConfirmedEmail</c>. The code default
/// (required outside Development and Testing) is pinned in <c>ServiceCollectionExtensionsTests</c>, but compose and k8s
/// pass the setting explicitly, so they — not the code — decide what a local stack does. Before this stage all three
/// shipped <c>false</c>, which was right while no email could be sent; now that Notification sends it, a stray
/// <c>false</c> would quietly let every new account sign in unconfirmed.
/// </summary>
[TestFixture]
public class ShippedConfirmationSettingsTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Test]
    public void Compose_RequiresConfirmation_UnlessTheEnvironmentSaysOtherwise()
    {
        Assert.That(Lines("docker-compose.yml"),
            Has.One.EqualTo("Identity__RequireConfirmedEmail: ${IDENTITY_REQUIRE_CONFIRMED_EMAIL:-true}"));
    }

    [Test]
    public void TheKubernetesConfigMap_RequiresConfirmation()
    {
        Assert.That(Lines("k8s/02-configmap.yaml"), Has.One.EqualTo("Identity__RequireConfirmedEmail: \"true\""));
    }

    [Test]
    public void EnvExample_RequiresConfirmation()
    {
        Assert.That(Lines(".env.example"), Has.One.EqualTo("IDENTITY_REQUIRE_CONFIRMED_EMAIL=true"));
    }

    /// <summary>The trimmed, uncommented lines of a tracked file.</summary>
    private static List<string> Lines(string file)
        => File.ReadAllLines(Path.Combine(RepositoryRoot, file))
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
