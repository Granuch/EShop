using EShop.ApiGateway.Telemetry;

namespace EShop.ApiGateway.UnitTests.Telemetry;

[TestFixture]
public sealed class GatewayRequestClassifierTests
{
    [TestCase(true, "simulation")]
    [TestCase(false, "proxy")]
    public void GetRequestMode_ReturnsExpectedMode(bool isSimulation, string expected)
    {
        var mode = GatewayRequestClassifier.GetRequestMode(isSimulation);

        Assert.That(mode, Is.EqualTo(expected));
    }

    [Test]
    public void IsRateLimitStatus_ReturnsTrueOnlyFor429()
    {
        Assert.That(GatewayRequestClassifier.IsRateLimitStatus(429), Is.True);
        Assert.That(GatewayRequestClassifier.IsRateLimitStatus(400), Is.False);
        Assert.That(GatewayRequestClassifier.IsRateLimitStatus(503), Is.False);
    }

    [Test]
    public void IsServerFailureStatus_ReturnsTrueFor5xx()
    {
        Assert.That(GatewayRequestClassifier.IsServerFailureStatus(500), Is.True);
        Assert.That(GatewayRequestClassifier.IsServerFailureStatus(503), Is.True);
        Assert.That(GatewayRequestClassifier.IsServerFailureStatus(429), Is.False);
    }
}
