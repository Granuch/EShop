using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.UnitTests.Infrastructure;

[TestFixture]
public class ServiceCollectionExtensionsTests
{
    // The old version of this test counted ServiceDescriptor entries for ILoginAttemptTracker
    // without ever building the provider — a double-registration behind a *different* service
    // type, or a registration that throws the moment something tries to resolve it, would both
    // pass. Building the provider and resolving the service is a strictly stronger check: it
    // also exercises the rest of the DI graph AddIdentityInfrastructure wires up (DbContext,
    // caching behaviors, ICacheInvalidationContext) rather than just this one registration.
    // ILoginAttemptTracker is registered `AddScoped`, so the correct invariant is "one instance
    // per scope, not shared across scopes" — the opposite of what a naive singleton assertion
    // would check, and worth pinning explicitly since brute-force tracking state leaking across
    // unrelated request scopes would be a real bug.
    [Test]
    public void AddIdentityInfrastructure_RegistersLoginAttemptTrackerAsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddIdentityInfrastructure(
            configuration,
            useInMemoryDatabase: true,
            inMemoryDatabaseName: $"IdentityUnitTestDb_{Guid.NewGuid()}",
            isDevelopment: true);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var trackerFromScopeA1 = scopeA.ServiceProvider.GetRequiredService<ILoginAttemptTracker>();
        var trackerFromScopeA2 = scopeA.ServiceProvider.GetRequiredService<ILoginAttemptTracker>();
        var trackerFromScopeB = scopeB.ServiceProvider.GetRequiredService<ILoginAttemptTracker>();

        Assert.That(trackerFromScopeA1, Is.Not.Null);
        Assert.That(trackerFromScopeA1, Is.SameAs(trackerFromScopeA2),
            "the same scope should resolve the same instance");
        Assert.That(trackerFromScopeA1, Is.Not.SameAs(trackerFromScopeB),
            "a different scope must not share brute-force tracking state");
    }

    /// <summary>
    /// Email-confirmation Stage 3: confirmation is required everywhere but Development and Testing — Sandbox included,
    /// now that Notification emails the link — and <c>Identity:RequireConfirmedEmail</c> overrides either way.
    /// </summary>
    [TestCase(false, null, true, TestName = "Sandbox, Production and k8s require it by default")]
    [TestCase(true, null, false, TestName = "Development and Testing do not by default")]
    [TestCase(false, "false", false, TestName = "The setting turns it off outside Development")]
    [TestCase(true, "true", true, TestName = "The setting turns it on in Development")]
    public void RequireConfirmedEmail_FollowsTheEnvironment_UnlessConfigured(
        bool isDevelopment, string? setting, bool expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        var values = new Dictionary<string, string?>();
        if (setting is not null)
            values["Identity:RequireConfirmedEmail"] = setting;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        services.AddIdentityInfrastructure(
            configuration,
            useInMemoryDatabase: true,
            inMemoryDatabaseName: $"IdentityUnitTestDb_{Guid.NewGuid()}",
            isDevelopment: isDevelopment);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>();

        Assert.That(options.Value.SignIn.RequireConfirmedEmail, Is.EqualTo(expected));
    }
}
