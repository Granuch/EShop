using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace EShop.Basket.IntegrationTests.Configuration;

/// <summary>
/// Basket audit M11, closed by S10. Every other host in this suite runs as Testing, where the startup checks do not act, so
/// these run <c>Program.cs</c> as Production or Sandbox. Before S10 the JWT check was length-only, so every placeholder
/// below (and the tracked Development key) booted Basket, and RabbitMQ and Redis placeholders passed as well.
///
/// <para><b>"Refused by the check" is told apart from "failed for another reason"</b> by <see cref="GuardedHostFactory.ReachedBuild"/>:
/// the checks run while <c>Program.cs</c> composes the host, and the factory's <c>ConfigureServices</c> callback runs only
/// when <c>Program.cs</c> reaches <c>builder.Build()</c>, after them. The clean-configuration controls must reach it and
/// start, or the refusals would prove nothing. Nothing is contacted: Redis and RabbitMQ point at <c>.invalid</c> hosts.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class StartupGuardTests
{
    private const string RealKey = "k7Qp2vXw9sLm4tRz8bNc6yHd3fJg5aUe1oWi0";

    /// <summary>What the tracked <c>appsettings.Development.json</c> ships: longer than 32 characters.</summary>
    private const string TrackedDevelopmentKey =
        "CHANGE_ME_generate_a_secure_key_at_least_32_characters_long_using_openssl_rand_base64_64";

    [TestCase("#{JWT_SECRET_KEY}#-and-enough-padding-to-pass")]
    [TestCase("CHANGE_ME_basket_jwt_secret_32_characters_x")]
    [TestCase("LOCAL_basket_jwt_secret_at_least_32_chars_x")]
    [TestCase("REPLACE_WITH_a_real_jwt_secret_of_32_chars")]
    [TestCase("YOUR_JWT_SECRET_GOES_HERE_at_least_32_chars")]
    [TestCase("basket-TestKey-jwt-secret-at-least-32-chars")]
    [TestCase("a-placeholder-jwt-secret-of-at-least-32-chars")]
    public void InProduction_TheHostRefusesToStart_WithAPlaceholderJwtKey(string key)
    {
        var (failure, reachedBuild) = Start("Production", key);

        reachedBuild.Should().BeFalse("the check runs while the host is composed, before it is built");
        Messages(failure).Should().Contain(m => m.Contains("JWT SecretKey contains placeholder pattern"));
    }

    [Test]
    public void InSandbox_TheTrackedDevelopmentJwtKey_IsRefused()
    {
        var (failure, reachedBuild) = Start("Sandbox", TrackedDevelopmentKey);

        reachedBuild.Should().BeFalse("Sandbox is deployed and reachable, so it is guarded like Production");
        Messages(failure).Should().Contain(m => m.Contains("JWT SecretKey contains placeholder pattern 'CHANGE_ME'"));
    }

    [Test]
    public void InProduction_TheHostRefusesToStart_WithAJwtKeyShorterThan32Characters()
    {
        var (failure, reachedBuild) = Start("Production", RealKey[..31]);

        reachedBuild.Should().BeFalse();
        Messages(failure).Should().Contain(m => m.Contains("at least 32 characters"));
    }

    /// <summary>The first three are what the tracked <c>appsettings.Production.json</c> ships when not substituted.</summary>
    [TestCase("ConnectionStrings:Redis", "#{REDIS_CONNECTION_STRING}#")]
    [TestCase("RabbitMQ:Host", "#{RABBITMQ_HOST}#")]
    [TestCase("RabbitMQ:Username", "#{RABBITMQ_USERNAME}#")]
    [TestCase("RabbitMQ:Password", "CHANGE_ME_rabbitmq_password")]
    public void InProduction_TheHostRefusesToStart_WithAPlaceholderConnectionSetting(string setting, string value)
    {
        var (failure, reachedBuild) = Start("Production", RealKey, (setting, value));

        reachedBuild.Should().BeFalse();
        Messages(failure).Should().Contain(m => m.Contains($"{setting} contains placeholder pattern"));
    }

    [Test]
    public void TheHostRefusesToStart_WithoutARedisConnectionString()
    {
        var (failure, reachedBuild) = Start("Production", RealKey, ("ConnectionStrings:Redis", ""));

        reachedBuild.Should().BeFalse("the tracked appsettings.json ships an empty one, which is not null");
        Messages(failure).Should().Contain(m => m.Contains("ConnectionStrings:Redis is required"));
    }

    [TestCase("Production")]
    [TestCase("Sandbox")]
    public void ACleanConfiguration_StartsTheHost(string environment)
    {
        var (failure, reachedBuild) = Start(environment, RealKey);

        reachedBuild.Should().BeTrue("real values must pass every check, or the refusals above prove nothing");
        failure.Should().BeNull();
    }

    private static (Exception? Failure, bool ReachedBuild) Start(
        string environment,
        string jwtKey,
        (string Key, string Value)? setting = null)
    {
        using var factory = new GuardedHostFactory(environment, jwtKey, setting);

        try
        {
            factory.CreateClient().Dispose();
            return (null, factory.ReachedBuild);
        }
        catch (Exception ex)
        {
            return (ex, factory.ReachedBuild);
        }
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

    private sealed class GuardedHostFactory(string environment, string jwtKey, (string Key, string Value)? setting)
        : BasketApiFactory
    {
        public bool ReachedBuild { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment(environment);
            builder.UseSetting("JwtSettings:SecretKey", jwtKey);
            builder.UseSetting("Cors:AllowedOrigins:0", "https://shop.eshop-real.test");
            builder.UseSetting("CatalogService:BaseUrl", "http://catalog-api:8080/");
            builder.UseSetting("ConnectionStrings:Redis", "redis.invalid:6379,abortConnect=false");
            builder.UseSetting("RabbitMQ:Host", "rabbitmq.invalid");
            builder.UseSetting("RabbitMQ:Username", "u");
            builder.UseSetting("RabbitMQ:Password", "p");
            builder.UseSetting("RabbitMQ:WaitUntilStarted", "false");

            if (setting is { } overridden)
            {
                builder.UseSetting(overridden.Key, overridden.Value);
            }

            // Runs when Program.cs calls builder.Build(), i.e. only once every startup check has passed.
            builder.ConfigureServices(_ => ReachedBuild = true);
        }
    }
}
