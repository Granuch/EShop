using EShop.Ordering.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Configuration;

/// <summary>
/// Ordering audit M7, closed by Stage 22 (D18). The JWT key check and <c>CorsOriginGuard</c> only act outside
/// Development and Testing, and every other host in this suite runs as Testing, so nothing could see them go:
/// deleting either call from <c>Program.cs</c> left the whole suite green. These tests run <c>Program.cs</c> as
/// <b>Production</b> and assert that each misconfiguration stops the host with that check's own message.
///
/// <para><b>How "stopped by the check" is told apart from "failed for another reason".</b> Both checks run
/// while <c>Program.cs</c> composes the host; the factory's <c>ConfigureServices</c> callbacks run only when
/// <c>Program.cs</c> reaches <c>builder.Build()</c>, after them. <see cref="ProductionHostFactory.ReachedBuild"/>
/// records that. A refused host never reaches it; the control case, with a clean configuration, does. That
/// control is what keeps the refusals honest — without it, a host failing early for an unrelated reason
/// would look exactly like a working check.</para>
///
/// <para>Nothing here is contacted: the database and RabbitMQ settings only have to be present, because
/// <c>Program.cs</c> requires them outside Development before it reaches the two checks.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class StartupGuardTests
{
    private const string RealKey = "k7Qp2vXw9sLm4tRz8bNc6yHd3fJg5aUe1oWi0";
    private const string RealOrigin = "https://shop.eshop-real.test";

    [TestCase("#{JWT_SECRET_KEY}#-and-enough-padding-to-pass")]
    [TestCase("CHANGE_ME_ordering_jwt_secret_32_characters")]
    [TestCase("LOCAL_ordering_jwt_secret_at_least_32_chars")]
    [TestCase("REPLACE_WITH_a_real_jwt_secret_of_32_chars")]
    [TestCase("YOUR_JWT_SECRET_GOES_HERE_at_least_32_chars")]
    [TestCase("ordering-TestKey-jwt-secret-at-least-32-chars")]
    [TestCase("a-placeholder-jwt-secret-of-at-least-32-chars")]
    public void InProduction_TheHostRefusesToStart_WithAPlaceholderJwtKey(string key)
    {
        var (failure, reachedBuild) = StartInProduction(key, RealOrigin);

        reachedBuild.Should().BeFalse("the check runs while the host is composed, before it is built");
        Messages(failure).Should().Contain(m => m.Contains("JWT SecretKey contains placeholder pattern"));
    }

    [Test]
    public void InProduction_TheHostRefusesToStart_WithAJwtKeyShorterThan32Characters()
    {
        var (failure, reachedBuild) = StartInProduction(RealKey[..31], RealOrigin);

        reachedBuild.Should().BeFalse();
        Messages(failure).Should().Contain(m => m.Contains("at least 32 characters"));
    }

    [Test]
    public void InProduction_TheHostRefusesToStart_WithAPlaceholderCorsOrigin()
    {
        // The placeholder the tracked appsettings.Production.json of several services ships.
        var (failure, reachedBuild) = StartInProduction(RealKey, "https://your-production-frontend.com");

        reachedBuild.Should().BeFalse();
        Messages(failure).Should().Contain(m => m.Contains("Cors:AllowedOrigins contains the placeholder origin"));
    }

    [Test]
    public void InProduction_ACleanConfiguration_GetsPastBothChecks()
    {
        var (failure, reachedBuild) = StartInProduction(RealKey, RealOrigin);

        reachedBuild.Should().BeTrue("a real key and a real origin must pass both checks, or the refusals above prove nothing");
        Messages(failure).Should().NotContain(m => m.Contains("JWT SecretKey") || m.Contains("Cors:AllowedOrigins"));
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
            // In the control case the host may fail after Build for reasons of its own (a relational
            // migration on the factory's in-memory database); only the checks' messages matter here.
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

    private sealed class ProductionHostFactory(string jwtKey, string origin) : OrderingApiFactory
    {
        public bool ReachedBuild { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment("Production");
            builder.UseSetting("JwtSettings:SecretKey", jwtKey);
            builder.UseSetting("Cors:AllowedOrigins:0", origin);

            // Required outside Development before Program.cs reaches the checks; never contacted.
            builder.UseSetting("ConnectionStrings:OrderingDb", "Host=ordering-db.invalid;Database=ordering;Username=u;Password=p");
            builder.UseSetting("RabbitMQ:Host", "rabbitmq.invalid");
            builder.UseSetting("RabbitMQ:Username", "u");
            builder.UseSetting("RabbitMQ:Password", "p");
            // Should a host ever get as far as starting, do not wait up to two minutes for a broker that
            // does not exist.
            builder.UseSetting("RabbitMQ:WaitUntilStarted", "false");
        }

        /// <summary>Runs when Program.cs calls <c>builder.Build()</c>, i.e. only once both checks have passed.</summary>
        protected override void ConfigureTestServices(IServiceCollection services) => ReachedBuild = true;
    }
}
