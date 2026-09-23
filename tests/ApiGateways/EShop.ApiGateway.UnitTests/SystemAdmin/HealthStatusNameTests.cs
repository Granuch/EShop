using EShop.ApiGateway.SystemAdmin;

namespace EShop.ApiGateway.UnitTests.SystemAdmin;

/// <summary>
/// Admin panel S19. The aggregate health read echoes a downstream status only when it is exactly a <c>HealthStatus</c>
/// name. <c>Enum.TryParse</c> would also accept a number — <c>"5"</c> is an undefined status — and any casing.
/// </summary>
[TestFixture]
public class HealthStatusNameTests
{
    [TestCase("Healthy")]
    [TestCase("Degraded")]
    [TestCase("Unhealthy")]
    public void AHealthStatusName_IsAccepted(string value)
        => Assert.That(SystemFanOut.IsHealthStatusName(value), Is.True);

    [TestCase("5")]
    [TestCase("2")]
    [TestCase("healthy")]
    [TestCase("Healthy ")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("Healthy, Degraded")]
    public void AnythingElse_IsNot(string? value)
        => Assert.That(SystemFanOut.IsHealthStatusName(value), Is.False);
}
