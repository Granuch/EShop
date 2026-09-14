using System.Text.RegularExpressions;

namespace EShop.Basket.IntegrationTests.Configuration;

/// <summary>
/// Basket audit C1 (D1): what the repository ships. No test host reads <c>docker-compose.yml</c> or the k8s
/// manifests, and the value is only missed at the first add-to-basket, so only a test that reads the files can
/// notice basket-api losing its Catalog URL again. Each check is scoped to basket-api's own block: ordering-api sets
/// the same variable, and a file-wide search would find that one instead.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ShippedCatalogSettingsTests
{
    private const string CatalogUrl = "http://catalog-api:8080/";
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Test]
    public void Compose_GivesBasketApiTheCatalogUrl()
    {
        var block = ComposeServiceBlock("basket-api");

        Assert.That(block.Where(line => line.StartsWith("CatalogService__BaseUrl:", StringComparison.Ordinal)),
            Is.EqualTo(new[] { "CatalogService__BaseUrl: " + CatalogUrl }));
    }

    [Test]
    public void Kubernetes_GivesBasketApiTheCatalogUrl()
    {
        var deployment = KubernetesDeployment("basket-api");
        var index = deployment.FindIndex(line => line == "- name: CatalogService__BaseUrl");

        Assert.That(index, Is.GreaterThanOrEqualTo(0), "basket-api's Deployment sets no CatalogService__BaseUrl");
        Assert.That(deployment.Count(line => line == "- name: CatalogService__BaseUrl"), Is.EqualTo(1));
        Assert.That(deployment[index + 1], Is.EqualTo($"value: \"{CatalogUrl}\""));
    }

    /// <summary>The uncommented, trimmed lines of one service in docker-compose.yml, up to the next service.</summary>
    private static List<string> ComposeServiceBlock(string service)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot, "docker-compose.yml"));
        var start = Array.IndexOf(lines, $"  {service}:");
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"docker-compose.yml has no {service} service");

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
