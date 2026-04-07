using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Payment.UnitTests.Services;

[TestFixture]
public class MockPaymentProcessorTests
{
    [Test]
    public async Task ProcessPaymentAsync_WhenSuccessRateIs100_ShouldAlwaysSucceed()
    {
        var processor = CreateProcessor(successRatePercent: 100);

        var result = await processor.ProcessPaymentAsync(Guid.NewGuid(), 100m);

        Assert.That(result.Success, Is.True);
        Assert.That(result.PaymentIntentId, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task ProcessPaymentAsync_WhenSuccessRateIs0_ShouldAlwaysFail()
    {
        var processor = CreateProcessor(successRatePercent: 0);

        var result = await processor.ProcessPaymentAsync(Guid.NewGuid(), 100m);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task ProcessPaymentAsync_WhenModeIsAlwaysSuccess_ShouldSucceedRegardlessOfSuccessRate()
    {
        var processor = CreateProcessor(
            successRatePercent: 0,
            mode: PaymentSimulationMode.AlwaysSuccess);

        var result = await processor.ProcessPaymentAsync(Guid.NewGuid(), 50m);

        Assert.That(result.Success, Is.True);
        Assert.That(result.PaymentIntentId, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task ProcessPaymentAsync_WhenModeIsAlwaysFailure_ShouldFailWithConfiguredReason()
    {
        const string forcedReason = "Forced test failure";
        var processor = CreateProcessor(
            successRatePercent: 100,
            mode: PaymentSimulationMode.AlwaysFailure,
            forcedFailureReason: forcedReason);

        var result = await processor.ProcessPaymentAsync(Guid.NewGuid(), 50m);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.EqualTo(forcedReason));
    }

    [Test]
    public async Task RefundPaymentAsync_ShouldReturnSuccessfulResult()
    {
        var processor = CreateProcessor(successRatePercent: 100);

        var result = await processor.RefundPaymentAsync("pi_test", 50m);

        Assert.That(result.Success, Is.True);
        Assert.That(result.PaymentIntentId, Is.EqualTo("pi_test"));
    }

    private static MockPaymentProcessor CreateProcessor(
        int successRatePercent,
        PaymentSimulationMode mode = PaymentSimulationMode.Random,
        int? randomSeed = 42,
        string forcedFailureReason = "Forced failure by simulation mode.")
    {
        var settings = Options.Create(new PaymentSimulationSettings
        {
            ProcessingDelayMinSeconds = 0,
            ProcessingDelayMaxSeconds = 0,
            SuccessRatePercent = successRatePercent,
            RefundDelaySeconds = 0,
            Mode = mode,
            RandomSeed = randomSeed,
            ForcedFailureReason = forcedFailureReason
        });

        return new MockPaymentProcessor(settings, Mock.Of<ILogger<MockPaymentProcessor>>());
    }
}
