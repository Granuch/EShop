namespace EShop.Identity.UnitTests.Configuration;

/// <summary>
/// What the repository ships for <c>Identity:RequireConfirmedEmail</c>. The code default (off) is pinned in
/// <c>ServiceCollectionExtensionsTests</c>, but compose and k8s pass the setting explicitly, so they — not the code —
/// decide what a stack does. Soft email verification turned all three to <c>false</c>: an unconfirmed account signs in,
/// and only placing an order needs the confirmed address. A stray <c>true</c> would quietly bring back strict mode, where
/// every new customer is locked out until they click the link.
/// </summary>
[TestFixture]
public class ShippedConfirmationSettingsTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Test]
    public void Compose_DoesNotRequireConfirmationToSignIn_UnlessTheEnvironmentSaysOtherwise()
    {
        Assert.That(Lines("docker-compose.yml"),
            Has.One.EqualTo("Identity__RequireConfirmedEmail: ${IDENTITY_REQUIRE_CONFIRMED_EMAIL:-false}"));
    }

    [Test]
    public void TheKubernetesConfigMap_DoesNotRequireConfirmationToSignIn()
    {
        Assert.That(Lines("k8s/02-configmap.yaml"), Has.One.EqualTo("Identity__RequireConfirmedEmail: \"false\""));
    }

    [Test]
    public void EnvExample_DoesNotRequireConfirmationToSignIn()
    {
        Assert.That(Lines(".env.example"), Has.One.EqualTo("IDENTITY_REQUIRE_CONFIRMED_EMAIL=false"));
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
