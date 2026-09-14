using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Configuration;

/// <summary>
/// Payment audit Stage 11 (M8), ported from Ordering's. The startup checks act only outside Development and Testing,
/// and every other host in this suite runs as Testing, so no other test could see one go. These run <c>Program.cs</c>
/// as <b>Production</b> and as <b>Sandbox</b>. Each misconfiguration must stop the host with its own check's message.
/// <para>Sandbox is checked since this stage. Payment used to exempt it from the JWT and connection-string checks,
/// so a placeholder key booted payment-api while the same shared key crash-looped Identity. The Stripe webhook bypass
/// is the one Sandbox exemption kept.</para>
/// <para><b>How "stopped by a check" is told apart from "failed for another reason".</b> The checks run while
/// <c>Program.cs</c> composes the host. The factory's <c>ConfigureServices</c> callback runs only when
/// <c>Program.cs</c> reaches <c>builder.Build()</c>, after them. A refused host never gets there. The clean
/// configurations do, and those controls are what make the refusals mean something.</para>
/// <para>Nothing is contacted. After the checks a clean host fails fast at its first migration, against a port nothing
/// listens on.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class StartupGuardTests
{
    private const string RealKey = "k7Qp2vXw9sLm4tRz8bNc6yHd3fJg5aUe1oWi0";
    private const string RealOrigin = "https://shop.eshop-real.test";
    private const string RealConnection = "Host=127.0.0.1;Port=1;Database=eshop_payment;Username=payment;Password=k7Qp2vXw9sLm";

    private sealed record Setup(
        string Environment,
        string JwtKey = RealKey,
        string Origin = RealOrigin,
        string Connection = RealConnection,
        bool WebhookBypass = false);

    [TestCase("#{JWT_SECRET_KEY}#-and-enough-padding-to-pass")]
    [TestCase("CHANGE_ME_payment_service_secret_key_32_chars_min")]
    [TestCase("LOCAL_payment_service_dev_secret_key_32_chars_min")]
    [TestCase("REPLACE_WITH_a_real_jwt_secret_of_32_chars")]
    [TestCase("YOUR_JWT_SECRET_GOES_HERE_at_least_32_chars")]
    [TestCase("payment-TestKey-jwt-secret-at-least-32-chars")]
    [TestCase("a-placeholder-jwt-secret-of-at-least-32-chars")]
    public void InProduction_APlaceholderJwtKey_StopsTheHost(string key)
        => AssertRefused(new Setup("Production", JwtKey: key), "JWT SecretKey contains placeholder pattern");

    [Test]
    public void InProduction_AJwtKeyShorterThan32Characters_StopsTheHost()
        => AssertRefused(new Setup("Production", JwtKey: RealKey[..31]), "at least 32 characters");

    /// <summary>The tracked Development key, which booted payment-api in Sandbox before this stage.</summary>
    [Test]
    public void InSandbox_APlaceholderJwtKey_StopsTheHost()
        => AssertRefused(
            new Setup("Sandbox", JwtKey: "LOCAL_payment_service_dev_secret_key_32_chars_min"),
            "JWT SecretKey contains placeholder pattern 'LOCAL_'");

    [Test]
    public void InSandbox_APlaceholderConnectionString_StopsTheHost()
        => AssertRefused(
            new Setup("Sandbox", Connection: "Host=payment-postgres;Port=5432;Database=eshop_payment;Username=postgres;Password=CHANGE_ME_payment_postgres_password"),
            "ConnectionStrings:PaymentDb contains placeholder pattern 'CHANGE_ME'");

    [Test]
    public void InProduction_ALocalhostConnectionString_StopsTheHost()
        => AssertRefused(
            new Setup("Production", Connection: "Host=localhost;Port=5436;Database=eshop_payment;Username=payment;Password=k7Qp2vXw9sLm"),
            "ConnectionStrings:PaymentDb contains localhost");

    /// <summary>The placeholder the tracked appsettings.Production.json of several services ships.</summary>
    [Test]
    public void InProduction_APlaceholderCorsOrigin_StopsTheHost()
        => AssertRefused(
            new Setup("Production", Origin: "https://your-production-frontend.com"),
            "Cors:AllowedOrigins contains the placeholder origin");

    [Test]
    public void InProduction_TheWebhookSignatureBypass_StopsTheHost()
        => AssertRefused(new Setup("Production", WebhookBypass: true), "bypass is only allowed");

    [TestCase("Production")]
    [TestCase("Sandbox")]
    public void ACleanConfiguration_GetsPastEveryCheck(string environment)
        => AssertStarted(new Setup(environment));

    /// <summary>Payment audit D5: the bypass stays allowed in Sandbox, where it logs a warning.</summary>
    [Test]
    public void InSandbox_TheWebhookSignatureBypass_IsStillAllowed()
        => AssertStarted(new Setup("Sandbox", WebhookBypass: true));

    private static void AssertRefused(Setup setup, string expectedMessage)
    {
        var (failure, reachedBuild) = Start(setup);
        var messages = Messages(failure).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(reachedBuild, Is.False, "the check runs while the host is composed, before it is built");
            Assert.That(messages.Any(m => m.Contains(expectedMessage, StringComparison.Ordinal)), Is.True,
                string.Join(" | ", messages));
        });
    }

    private static void AssertStarted(Setup setup)
    {
        var (failure, reachedBuild) = Start(setup);
        var messages = Messages(failure).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(reachedBuild, Is.True,
                "a clean configuration must pass every check, or the refusals prove nothing: " + string.Join(" | ", messages));
            Assert.That(
                messages.Any(m => m.Contains("JWT SecretKey") || m.Contains("Cors:AllowedOrigins")
                                  || m.Contains("ConnectionStrings:PaymentDb") || m.Contains("bypass is only allowed")),
                Is.False,
                string.Join(" | ", messages));
        });
    }

    private static (Exception? Failure, bool ReachedBuild) Start(Setup setup)
    {
        using var factory = new GuardedHostFactory(setup);
        Exception? failure = null;

        try
        {
            factory.CreateClient().Dispose();
        }
        catch (Exception ex)
        {
            // A clean host fails after Build, at its first migration; only the checks' messages matter here.
            failure = ex;
        }

        return (failure, factory.ReachedBuild);
    }

    private static IEnumerable<string> Messages(Exception? e)
    {
        for (var current = e; current is not null; current = current.InnerException)
        {
            yield return current.Message;

            if (current is AggregateException aggregate)
            {
                foreach (var message in aggregate.InnerExceptions.SelectMany(Messages))
                {
                    yield return message;
                }
            }
        }
    }

    private sealed class GuardedHostFactory(Setup setup) : PaymentApiFactory
    {
        public bool ReachedBuild { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment(setup.Environment);
            builder.UseSetting("JwtSettings:SecretKey", setup.JwtKey);
            builder.UseSetting("Cors:AllowedOrigins:0", setup.Origin);
            builder.UseSetting("ConnectionStrings:PaymentDb", setup.Connection);
            builder.UseSetting("Stripe:SkipWebhookSignatureVerification", setup.WebhookBypass ? "true" : "false");

            // Required outside Development before Program.cs reaches the checks; never contacted.
            builder.UseSetting("RabbitMQ:Host", "127.0.0.1");
            builder.UseSetting("RabbitMQ:Username", "u");
            builder.UseSetting("RabbitMQ:Password", "p");
            builder.UseSetting("RabbitMQ:WaitUntilStarted", "false");

            // Runs when Program.cs calls builder.Build(), i.e. only once every check has passed.
            builder.ConfigureServices(_ => ReachedBuild = true);
        }
    }
}
