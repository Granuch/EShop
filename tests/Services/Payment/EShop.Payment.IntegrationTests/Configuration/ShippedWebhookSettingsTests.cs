namespace EShop.Payment.IntegrationTests.Configuration;

/// <summary>
/// Payment audit Stage 4 (H3, D5): what the repository ships. compose and k8s used to default both webhook bypass
/// flags to true, and compose published payment-api on every interface, so anyone who could reach the host could
/// POST a hand-written <c>payment_intent.succeeded</c> once Stripe was enabled. No test host reads these files,
/// so only a test that reads them can notice a default flipped back.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ShippedWebhookSettingsTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestCase("Stripe__SkipWebhookSignatureVerification")]
    [TestCase("Stripe__AllowMissingSignatureHeaderInBypassMode")]
    public void Compose_DefaultsTheWebhookBypassOff(string key)
    {
        Assert.That(TheOneLine("docker-compose.yml", key + ":"), Does.Match(@":-false\}$"));
    }

    [TestCase("Stripe__SkipWebhookSignatureVerification")]
    [TestCase("Stripe__AllowMissingSignatureHeaderInBypassMode")]
    public void Kubernetes_TurnsTheWebhookBypassOff(string key)
    {
        Assert.That(TheOneLine("k8s/02-configmap.yaml", key + ":"), Is.EqualTo(key + @": ""false"""));
    }

    [TestCase("STRIPE_SKIP_WEBHOOK_SIGNATURE_VERIFICATION")]
    [TestCase("STRIPE_ALLOW_MISSING_SIGNATURE_HEADER_IN_BYPASS_MODE")]
    public void TheExampleEnvironment_TurnsTheWebhookBypassOff(string key)
    {
        Assert.That(TheOneLine(".env.example", key + "="), Is.EqualTo(key + "=false"));
    }

    [Test]
    public void Compose_PublishesPaymentApiOnLoopbackOnly()
    {
        Assert.That(TheOneLine("docker-compose.yml", @"- ""${PAYMENT_API_PORT", @"- ""127.0.0.1:${PAYMENT_API_PORT"),
            Is.EqualTo(@"- ""127.0.0.1:${PAYMENT_API_PORT:-7008}:8080"""));
    }

    /// <summary>
    /// The single uncommented line starting with any of <paramref name="prefixes"/>, trimmed. Exactly one, so a second
    /// definition cannot hide behind the one checked.
    /// </summary>
    private static string TheOneLine(string relativePath, params string[] prefixes)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot, relativePath))
            .Select(line => line.Trim())
            .Where(line => prefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.That(lines, Has.Count.EqualTo(1), $"{relativePath}: expected one line starting with {string.Join(" or ", prefixes)}");
        return lines[0];
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
