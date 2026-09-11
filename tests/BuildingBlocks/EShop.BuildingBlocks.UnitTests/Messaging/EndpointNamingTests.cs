using EShop.BuildingBlocks.Infrastructure.Extensions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.UnitTests.Messaging
{
    /// <summary>
    /// Every service shares one RabbitMQ vhost, so a queue name is global. Before the service prefix,
    /// queues were named by consumer class alone: Payment's and Notification's
    /// <c>OrderCreatedConsumer</c> both bound <c>order_created</c>, and RabbitMQ load-balanced it —
    /// on a real 3.13 broker, 20 published messages reached each service 10 times. Nothing failed and
    /// nothing logged. These pin the naming rule; the broker run is recorded in the audit log.
    /// </summary>
    [TestFixture]
    public class EndpointNamingTests
    {
        [Test]
        public void SameNamedConsumersInTwoServices_GetTwoQueues()
        {
            var payment = MassTransitServiceCollectionExtensions.CreateEndpointNameFormatter("payment")
                .Consumer<PaymentFixture.OrderCreatedConsumer>();
            var notification = MassTransitServiceCollectionExtensions.CreateEndpointNameFormatter("notification")
                .Consumer<NotificationFixture.OrderCreatedConsumer>();

            Assert.That(payment, Is.Not.EqualTo(notification));
        }

        /// <summary>
        /// Pins the exact shape too: operators delete the pre-prefix queues by name, and the deploy
        /// note lists the new ones, so the format is part of the operational contract.
        /// </summary>
        [Test]
        public void TheQueueIsTheServiceNameThenTheSnakeCaseConsumerName()
        {
            var name = MassTransitServiceCollectionExtensions.CreateEndpointNameFormatter("payment")
                .Consumer<PaymentFixture.OrderCreatedConsumer>();

            Assert.That(name, Is.EqualTo("payment_order_created"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void AddMessaging_RejectsAMissingServiceName_EvenWithoutABroker(string? serviceName)
        {
            // No RabbitMQ section at all: the name must still be demanded, or a service could ship
            // without one and only fail where a broker is configured.
            var configuration = new ConfigurationBuilder().Build();

            Assert.That(
                () => new ServiceCollection().AddMessaging<DummyDbContext>(configuration, serviceName!, isDevelopment: true),
                Throws.InstanceOf<ArgumentException>());
        }

        /// <summary>
        /// The factory alone proves nothing if <c>AddMessaging</c> stopped using it, so resolve the
        /// formatter the bus was actually configured with.
        /// </summary>
        [Test]
        public void AddMessaging_ConfiguresTheBusWithTheServicePrefix()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RabbitMQ:Host"] = "localhost",
                    ["RabbitMQ:Username"] = "u",
                    ["RabbitMQ:Password"] = "p",
                })
                .Build();

            using var provider = new ServiceCollection()
                .AddLogging()
                .AddMessaging<DummyDbContext>(configuration, "payment", isDevelopment: true)
                .BuildServiceProvider();

            var formatter = provider.GetRequiredService<IEndpointNameFormatter>();

            Assert.That(formatter.Consumer<PaymentFixture.OrderCreatedConsumer>(), Is.EqualTo("payment_order_created"));
        }

        private sealed class DummyDbContext : DbContext;
    }
}

namespace EShop.BuildingBlocks.UnitTests.Messaging.PaymentFixture
{
    public sealed record OrderCreated;

    public sealed class OrderCreatedConsumer : IConsumer<OrderCreated>
    {
        public Task Consume(ConsumeContext<OrderCreated> context) => Task.CompletedTask;
    }
}

namespace EShop.BuildingBlocks.UnitTests.Messaging.NotificationFixture
{
    public sealed record OrderCreated;

    public sealed class OrderCreatedConsumer : IConsumer<OrderCreated>
    {
        public Task Consume(ConsumeContext<OrderCreated> context) => Task.CompletedTask;
    }
}
