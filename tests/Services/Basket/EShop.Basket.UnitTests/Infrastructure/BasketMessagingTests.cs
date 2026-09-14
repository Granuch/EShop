using EShop.Basket.Infrastructure.Consumers;
using EShop.Basket.Infrastructure.Extensions;
using EShop.Basket.Infrastructure.Outbox;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.UnitTests.Infrastructure;

/// <summary>
/// Basket audit S11 (debt 5). Basket's bus is the shared one (<c>AddEShopBus</c>) — until S11 it was a hand-written copy
/// of <c>AddMessaging</c>'s block, because that method needs a DbContext. These pin what <c>AddBasketMessaging</c> must
/// still do on top of it: the <c>basket_</c> queue prefix, the Redis outbox processor when there is a broker, and the
/// warning instead of it when there is none.
/// </summary>
[TestFixture]
public class BasketMessagingTests
{
    private static IConfiguration WithBroker() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = "localhost",
            ["RabbitMQ:Username"] = "u",
            ["RabbitMQ:Password"] = "p",
        })
        .Build();

    /// <summary>The formatter the bus was actually configured with, not the factory's output.</summary>
    [Test]
    public void WithABroker_TheConsumerQueueCarriesTheBasketPrefix_AndTheOutboxIsProcessed()
    {
        var services = new ServiceCollection().AddLogging().AddBasketMessaging(WithBroker(), isDevelopment: false);
        using var provider = services.BuildServiceProvider();

        Assert.That(
            provider.GetRequiredService<IEndpointNameFormatter>().Consumer<ProductPriceChangedConsumer>(),
            Is.EqualTo("basket_product_price_changed"));
        Assert.That(services.Any(d => d.ImplementationType == typeof(BasketRedisOutboxProcessorService)), Is.True);
        Assert.That(services.Any(d => d.ImplementationType == typeof(OutboxNotDrainedWarning)), Is.False);
    }

    [Test]
    public void WithoutABroker_InDevelopment_ThereIsNoBus_AndTheWarningRunsInsteadOfTheProcessor()
    {
        var services = new ServiceCollection().AddLogging()
            .AddBasketMessaging(new ConfigurationBuilder().Build(), isDevelopment: true);

        Assert.That(services.Any(d => d.ServiceType == typeof(IBusControl)), Is.False);
        Assert.That(services.Any(d => d.ImplementationType == typeof(OutboxNotDrainedWarning)), Is.True);
        Assert.That(services.Any(d => d.ImplementationType == typeof(BasketRedisOutboxProcessorService)), Is.False);
    }

    [Test]
    public void WithoutABroker_OutsideDevelopment_TheHostIsRefused()
    {
        Assert.That(
            () => new ServiceCollection().AddBasketMessaging(new ConfigurationBuilder().Build(), isDevelopment: false),
            Throws.InstanceOf<InvalidOperationException>());
    }
}
