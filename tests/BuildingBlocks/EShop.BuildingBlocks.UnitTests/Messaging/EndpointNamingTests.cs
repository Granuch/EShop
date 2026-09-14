using EShop.BuildingBlocks.Application.Abstractions;
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

        private static IConfiguration WithBroker() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "localhost",
                ["RabbitMQ:Username"] = "u",
                ["RabbitMQ:Password"] = "p",
            })
            .Build();

        /// <summary>
        /// Basket audit S11 (debt 5): <c>AddEShopBus</c> is the bus without the EF outbox, for a service with no
        /// DbContext. It reports whether it configured one, so such a service can say what it does without a broker.
        /// </summary>
        [Test]
        public void AddEShopBus_ReportsWhetherItConfiguredABus()
        {
            var withoutBroker = new ServiceCollection();
            var withBroker = new ServiceCollection().AddLogging();

            Assert.That(withoutBroker.AddEShopBus(new ConfigurationBuilder().Build(), "basket", isDevelopment: true), Is.False);
            Assert.That(withoutBroker.Any(d => d.ServiceType == typeof(IBusControl)), Is.False);
            Assert.That(withBroker.AddEShopBus(WithBroker(), "basket", isDevelopment: false), Is.True);
            Assert.That(withBroker.Any(d => d.ServiceType == typeof(IBusControl)), Is.True);
        }

        [Test]
        public void AddEShopBus_OutsideDevelopment_RefusesAMissingBroker()
        {
            Assert.That(
                () => new ServiceCollection().AddEShopBus(new ConfigurationBuilder().Build(), "basket", isDevelopment: false),
                Throws.InstanceOf<InvalidOperationException>());
        }

        /// <summary>Splitting the bus out must not cost the EF services their outbox, with a broker or without one.</summary>
        [Test]
        public void AddMessaging_StillRegistersTheOutbox_WithOrWithoutABroker()
        {
            var withoutBroker = new ServiceCollection().AddLogging()
                .AddMessaging<DummyDbContext>(new ConfigurationBuilder().Build(), "payment", isDevelopment: true);
            var withBroker = new ServiceCollection().AddLogging()
                .AddMessaging<DummyDbContext>(WithBroker(), "payment", isDevelopment: true);

            Assert.That(withoutBroker.Any(d => d.ServiceType == typeof(IIntegrationEventOutbox)), Is.True);
            Assert.That(withBroker.Any(d => d.ServiceType == typeof(IIntegrationEventOutbox)), Is.True);
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
