using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.UnitTests.Consumers;

/// <summary>
/// frontend-contracts F-47. A consumer that exists and is tested but is not added to the bus handles nothing: its queue
/// is never created, Ordering's <c>OrderTotalChangedEvent</c> is published to an exchange nobody is bound to, and every
/// consumer test stays green because each one calls <c>Consume</c> directly. This reads Payment's own messaging
/// registration, as production composes it, with a broker configured but never contacted.
/// </summary>
[TestFixture]
public class ConsumerRegistrationTests
{
    [TestCase(typeof(OrderCreatedConsumer))]
    [TestCase(typeof(OrderCancelledConsumer))]
    [TestCase(typeof(OrderTotalChangedConsumer))]
    public void PaymentsBus_RegistersTheConsumer(Type consumer)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "rabbitmq.invalid",
                ["RabbitMQ:Username"] = "user",
                ["RabbitMQ:Password"] = "password"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddPaymentMessaging(configuration, isDevelopment: false);

        Assert.That(services.Any(d => d.ServiceType == consumer), Is.True, $"{consumer.Name} is not added to Payment's bus");
    }
}
