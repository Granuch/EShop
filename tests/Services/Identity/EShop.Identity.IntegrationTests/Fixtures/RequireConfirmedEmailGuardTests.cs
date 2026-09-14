using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Fixtures;

/// <summary>
/// BUG-03 rail. Email confirmation is deliberately parked scaffolding: RegisterCommandHandler
/// mints a confirmation token and discards it, so nothing can ever deliver one. That is
/// harmless while SignIn.RequireConfirmedEmail is false — which it is in Development, Sandbox
/// and Testing — and catastrophic the first time it is true, because every account registered
/// from then on is permanently unable to log in with no way to mint a token.
///
/// The feature is not being finished here (scope decision: safety rails only). Instead the host
/// refuses to start in that configuration, so the trap cannot be walked into silently. This
/// test is what keeps the rail honest; without it the guard is a comment.
/// </summary>
[TestFixture]
public class RequireConfirmedEmailGuardTests
{
    private sealed class ConfirmedEmailRequiredFactory : IdentityApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Host configuration, not ConfigureAppConfiguration: Program.cs reads this while
            // composing the app, before an app-configuration source would be applied.
            builder.UseSetting("Identity:RequireConfirmedEmail", "true");
            base.ConfigureWebHost(builder);
        }
    }

    [Test]
    public void Startup_WhenRequireConfirmedEmailIsOnButNoTokenIsDelivered_ShouldFail()
    {
        using var factory = new ConfirmedEmailRequiredFactory();

        var thrown = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        var messages = Unwrap(thrown!).Select(e => e.Message).ToList();
        messages.Should().Contain(m => m.Contains("RequireConfirmedEmail"),
            "the failure has to name the setting, or the operator cannot act on it");
        messages.Should().Contain(m => m.Contains("UserRegisteredIntegrationEvent"),
            "and it has to name what is missing, or the fix is a guessing game");
    }

    [Test]
    public void Startup_WithTheDefaultTestingConfiguration_ShouldSucceed()
    {
        // The guard must not fire where confirmation is legitimately off — otherwise it would
        // have taken the whole suite down rather than protecting a Production deploy.
        using var factory = new IdentityApiFactory();

        Assert.DoesNotThrow(() => factory.CreateClient());
    }

    private static IEnumerable<Exception> Unwrap(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
