using EShop.Ordering.Infrastructure.Consumers;
using EShop.Ordering.Infrastructure.Extensions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.Ordering.UnitTests.Infrastructure;

[TestFixture]
public class OrderingMessagingStartupOptionsTests
{
    [Test]
    public void AddOrderingMessaging_ShouldConfigureMassTransitHostOptions_FromSharedAddMessaging()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "rabbitmq",
                ["RabbitMQ:Port"] = "5672",
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest",
                ["RabbitMQ:WaitUntilStarted"] = "true",
                ["RabbitMQ:StartTimeoutSeconds"] = "120"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddOrderingMessaging(configuration, isDevelopment: false);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MassTransitHostOptions>>().Value;

        Assert.That(options.WaitUntilStarted, Is.True);
        Assert.That(options.StartTimeout, Is.EqualTo(TimeSpan.FromSeconds(120)));
        Assert.That(options.StopTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    /// <summary>
    /// Every consumer Ordering relies on is registered on the bus. No test host can tell: under Testing
    /// <c>RabbitMQ:Host</c> is blank and <c>AddMessaging</c> registers no bus at all, so a consumer
    /// missing from <c>AddOrderingMessaging</c> would never receive a message and nothing would fail.
    /// <see cref="PaymentRefundedConsumer"/> was added in Ordering audit Stage 11.
    /// </summary>
    [TestCase(typeof(BasketCheckedOutConsumer))]
    [TestCase(typeof(PaymentSuccessConsumer))]
    [TestCase(typeof(PaymentFailedConsumer))]
    [TestCase(typeof(PaymentRefundedConsumer))]
    public void AddOrderingMessaging_RegistersConsumer(Type consumer)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "rabbitmq",
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddOrderingMessaging(configuration, isDevelopment: false);

        Assert.That(services.Any(d => d.ServiceType == consumer), Is.True,
            $"{consumer.Name} is not registered by AddOrderingMessaging.");
    }
}
