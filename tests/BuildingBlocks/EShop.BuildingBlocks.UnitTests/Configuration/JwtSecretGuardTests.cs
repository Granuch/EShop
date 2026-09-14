using EShop.BuildingBlocks.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace EShop.BuildingBlocks.UnitTests.Configuration;

/// <summary>
/// Ordering audit Stage 22 (D18). The JWT key check was copied into four services and the copies drifted —
/// two of them missed <c>LOCAL_</c> and <c>REPLACE_WITH_</c> — so the list is pinned here pattern by pattern:
/// dropping one from <see cref="JwtSecretGuard"/> turns its case red.
/// </summary>
[TestFixture]
public class JwtSecretGuardTests
{
    private const string RealKey = "k7Qp2vXw9sLm4tRz8bNc6yHd3fJg5aUe1oWi0";

    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(name);
        return environment.Object;
    }

    [TestCase("#{JWT_SECRET_KEY}#-and-enough-padding-to-pass")]
    [TestCase("CHANGE_ME_ordering_jwt_secret_32_characters")]
    [TestCase("LOCAL_ordering_jwt_secret_at_least_32_chars")]
    [TestCase("REPLACE_WITH_a_real_jwt_secret_of_32_chars")]
    [TestCase("YOUR_JWT_SECRET_GOES_HERE_at_least_32_chars")]
    [TestCase("ordering-TestKey-jwt-secret-at-least-32-chars")]
    [TestCase("a-placeholder-jwt-secret-of-at-least-32-chars")]
    public void Production_RefusesEveryPlaceholderPattern(string key)
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => JwtSecretGuard.Validate(key, Environment("Production")));

        Assert.That(thrown!.Message, Does.Contain("placeholder pattern"));
    }

    [Test]
    public void ThePatternList_IsTheSevenIdentityChecks()
    {
        Assert.That(JwtSecretGuard.PlaceholderPatterns,
            Is.EquivalentTo(new[] { "#{", "CHANGE_ME", "LOCAL_", "REPLACE_WITH_", "YOUR_", "TestKey", "placeholder" }));
    }

    [Test]
    public void Patterns_MatchCaseInsensitively()
    {
        Assert.Throws<InvalidOperationException>(
            () => JwtSecretGuard.Validate("change_me_ordering_jwt_secret_32_characters", Environment("Production")));
    }

    [Test]
    public void Sandbox_IsNotExempt()
    {
        // Sandbox is deployed and reachable, so it is held to Production's standard, as CorsOriginGuard does.
        Assert.Throws<InvalidOperationException>(
            () => JwtSecretGuard.Validate("CHANGE_ME_ordering_jwt_secret_32_characters", Environment("Sandbox")));
    }

    [TestCase("Development")]
    [TestCase("Testing")]
    public void DevelopmentAndTesting_TolerateAPlaceholder(string environmentName)
    {
        const string placeholder = "CHANGE_ME_ordering_jwt_secret_32_characters";

        Assert.That(JwtSecretGuard.Validate(placeholder, Environment(environmentName)), Is.EqualTo(placeholder));
    }

    [TestCase("Development")]
    [TestCase("Testing")]
    [TestCase("Production")]
    public void AMissingKey_IsRefusedEverywhere(string environmentName)
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => JwtSecretGuard.Validate("  ", Environment(environmentName)));

        Assert.That(thrown!.Message, Does.Contain("not configured"));
    }

    [TestCase("Development")]
    [TestCase("Production")]
    public void AKeyShorterThan32Characters_IsRefusedEverywhere(string environmentName)
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => JwtSecretGuard.Validate(RealKey[..31], Environment(environmentName)));

        Assert.That(thrown!.Message, Does.Contain("at least 32 characters"));
    }

    [Test]
    public void Production_WithARealKeyOfExactly32Characters_ReturnsIt()
    {
        var key = RealKey[..32];

        Assert.That(JwtSecretGuard.Validate(key, Environment("Production")), Is.EqualTo(key));
    }
}
