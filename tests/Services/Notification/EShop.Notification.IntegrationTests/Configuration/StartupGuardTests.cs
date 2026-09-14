using EShop.Notification.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;

namespace EShop.Notification.IntegrationTests.Configuration;

/// <summary>
/// Notification audit S4 (M9, M10; D4). Every other host in this suite runs as Testing, so these boot <c>Program.cs</c>
/// as Production or Sandbox to show that it calls <c>NotificationConfigurationGuard</c> before the host is built; the
/// rules themselves are pinned in <c>NotificationConfigurationGuardTests</c>.
///
/// <para><b>"Refused by the guard" is told apart from "failed for another reason"</b> by
/// <see cref="GuardedHostFactory.ReachedBuild"/>, as in Basket's and Ordering's tests: the guard runs while
/// <c>Program.cs</c> composes the host, and the factory's <c>ConfigureServices</c> callback runs only when it reaches
/// <c>builder.Build()</c>. The controls boot fully — migrations included, on a real Postgres database — or the refusals
/// would prove nothing. RabbitMQ points at an <c>.invalid</c> host and is not waited for.</para>
/// </summary>
[TestFixture]
public class StartupGuardTests
{
    private const string UnreachableDatabase = "Host=notification-db.invalid;Database=x;Username=x;Password=x";

    /// <summary>The tracked default until S4, which every deployed environment used to inherit.</summary>
    [Test]
    public void InProduction_TheOldTrackedResetUrl_StopsTheHost()
    {
        var (failure, reachedBuild) = Start("Production", UnreachableDatabase,
            ("PasswordReset:ResetUrlBase", "https://localhost:3000/reset-password"));

        Assert.That(reachedBuild, Is.False, "the guard runs while the host is composed, before it is built");
        Assert.That(Messages(failure), Has.Some.Contains("PasswordReset:ResetUrlBase points at localhost"));
    }

    [Test]
    public void InProduction_AMissingResetUrl_StopsTheHost()
    {
        var (failure, reachedBuild) = Start("Production", UnreachableDatabase, ("PasswordReset:ResetUrlBase", ""));

        Assert.That(reachedBuild, Is.False);
        Assert.That(Messages(failure), Has.Some.Contains("PasswordReset:ResetUrlBase is required"));
    }

    /// <summary>What the tracked appsettings.Development.json ships.</summary>
    [Test]
    public void InSandbox_ThePlaceholderApiKey_StopsTheHost()
    {
        var (failure, reachedBuild) = Start("Sandbox", UnreachableDatabase,
            ("IdentityService:ApiKey", "LOCAL_internal_service_api_key"));

        Assert.That(reachedBuild, Is.False, "Sandbox is deployed and reachable, so it is guarded like Production");
        Assert.That(Messages(failure), Has.Some.Contains("IdentityService:ApiKey contains placeholder pattern 'LOCAL_'"));
    }

    /// <summary>The Sandbox control uses the compose and k8s reset URL, which D4 allows outside Production.</summary>
    [TestCase("Production", "https://shop.eshop-real.test/reset-password")]
    [TestCase("Sandbox", "http://localhost:3000/reset-password")]
    public async Task ACleanConfiguration_StartsTheHost(string environment, string resetUrl)
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var (failure, reachedBuild) = Start(environment, connectionString, ("PasswordReset:ResetUrlBase", resetUrl));

            Assert.That(reachedBuild, Is.True, "real values must pass every check, or the refusals above prove nothing");
            Assert.That(failure, Is.Null, failure?.ToString());
        }
        finally
        {
            PostgresTestServer.ReleaseDatabase(connectionString);
        }
    }

    private static (Exception? Failure, bool ReachedBuild) Start(
        string environment,
        string connectionString,
        (string Key, string Value) setting)
    {
        using var factory = new GuardedHostFactory(environment, connectionString, setting);

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

    private sealed class GuardedHostFactory(string environment, string connectionString, (string Key, string Value) setting)
        : NotificationApiFactory
    {
        public bool ReachedBuild { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:NotificationDb", connectionString);
            builder.UseSetting("IdentityService:ApiKey", "k7Qp2vXw9sLm4tRz8bNc6yHd3fJg5aUe1oWi0");
            builder.UseSetting("RabbitMQ:Host", "rabbitmq.invalid");
            builder.UseSetting("RabbitMQ:Username", "u");
            builder.UseSetting("RabbitMQ:Password", "p");
            builder.UseSetting("RabbitMQ:WaitUntilStarted", "false");
            builder.UseSetting(setting.Key, setting.Value);

            // Runs when Program.cs calls builder.Build(), i.e. only once the guard has passed.
            builder.ConfigureServices(_ => ReachedBuild = true);
        }
    }
}
