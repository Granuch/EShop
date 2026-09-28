using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace EShop.ApiGateway.IntegrationTests.Configuration;

/// <summary>
/// docker-ci DC-36. The gateway was the one component that called neither <c>JwtSecretGuard</c> nor
/// <c>CorsOriginGuard</c>: under the production override, with the <c>.env.example</c> values, all six services
/// refused to start and the gateway served traffic, validating tokens with the public <c>CHANGE_ME_…</c> key. Both
/// guards act only outside Development and Testing, and every other host in this suite runs as Testing, so these
/// tests boot <c>Program.cs</c> as <b>Production</b> (the pattern of Ordering's <c>StartupGuardTests</c>).
///
/// <para><b>How "stopped by the guard" is told apart from "failed for another reason".</b> The guards run while
/// <c>Program.cs</c> composes the host. The factory's test-services callback runs only when <c>Program.cs</c>
/// reaches <c>builder.Build()</c>, after them, and <see cref="ProductionHostFactory.ReachedBuild"/> records that. A
/// refused host never reaches it; the clean-configuration control does. Without that control, a host failing
/// early for an unrelated reason would look exactly like a working guard.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class StartupGuardTests
{
    private const string RealKey = "k7Qp2vXw9sLm4tRz8bNc6yHd3fJg5aUe1oWi0";
    private const string RealOrigin = "https://shop.eshop-real.test";

    [TestCase("CHANGE_ME_jwt_secret_key_32_chars_min")] // .env.example's JWT_SECRET_KEY, the value DC-36 booted on
    [TestCase("CHANGE_ME_gateway_jwt_secret_key_32_chars_min")] // the tracked appsettings.Development.json
    [TestCase("#{JWT_SECRET_KEY}#-and-enough-padding-to-pass")]
    [TestCase("LOCAL_gateway_jwt_secret_key_32_chars_min")]
    public void InProduction_TheHostRefusesToStart_WithAPlaceholderJwtKey(string key)
    {
        var (failure, reachedBuild) = StartInProduction(key, RealOrigin);

        Assert.Multiple(() =>
        {
            Assert.That(reachedBuild, Is.False, "the guard runs while the host is composed, before it is built");
            Assert.That(Messages(failure), Has.Some.Contains("JWT SecretKey contains placeholder pattern"));
        });
    }

    [Test]
    public void InProduction_TheHostRefusesToStart_WithAPlaceholderCorsOrigin()
    {
        var (failure, reachedBuild) = StartInProduction(RealKey, "https://your-production-frontend.com");

        Assert.Multiple(() =>
        {
            Assert.That(reachedBuild, Is.False);
            Assert.That(Messages(failure), Has.Some.Contains("Cors:AllowedOrigins contains the placeholder origin"));
        });
    }

    [Test]
    public void InProduction_ACleanConfiguration_GetsPastBothGuards()
    {
        var (failure, reachedBuild) = StartInProduction(RealKey, RealOrigin);

        Assert.Multiple(() =>
        {
            Assert.That(reachedBuild, Is.True,
                "a real key and a real origin must pass both guards, or the refusals above prove nothing");
            Assert.That(Messages(failure), Has.None.Contains("JWT SecretKey"));
            Assert.That(Messages(failure), Has.None.Contains("Cors:AllowedOrigins"));
        });
    }

    private static (Exception? Failure, bool ReachedBuild) StartInProduction(string jwtKey, string origin)
    {
        using var factory = new ProductionHostFactory(jwtKey, origin);
        Exception? failure = null;

        try
        {
            factory.CreateClient().Dispose();
        }
        catch (Exception ex)
        {
            // Only the guards' messages matter; a host that gets past them may still fail for reasons of its own.
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

    private sealed class ProductionHostFactory(string jwtKey, string origin) : WebApplicationFactory<Program>
    {
        public bool ReachedBuild { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            // UseSetting, not ConfigureAppConfiguration: Program.cs reads both values while composing the host.
            builder.UseSetting("JwtSettings:SecretKey", jwtKey);
            builder.UseSetting("JwtSettings:Issuer", "EShop.Identity");
            builder.UseSetting("JwtSettings:Audience", "EShop.Services");
            builder.UseSetting("Cors:AllowedOrigins:0", origin);

            // Runs when Program.cs calls builder.Build(), i.e. only once both guards have passed.
            builder.ConfigureTestServices(_ => ReachedBuild = true);
        }
    }
}
