using EShop.Basket.Infrastructure.Extensions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.Basket.UnitTests.Infrastructure;

[TestFixture]
public class BasketMessagingStartupOptionsTests
{
    [Test]
    public void AddBasketMessaging_ShouldConfigureMassTransitHostOptions_FromRabbitMqSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "rabbitmq",
                ["RabbitMQ:Port"] = "5672",
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest",
                ["RabbitMQ:WaitUntilStarted"] = "true",
                ["RabbitMQ:StartTimeoutSeconds"] = "90"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddBasketMessaging(configuration, isDevelopment: false);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MassTransitHostOptions>>().Value;

        Assert.That(options.WaitUntilStarted, Is.True);
        Assert.That(options.StartTimeout, Is.EqualTo(TimeSpan.FromSeconds(90)));
        Assert.That(options.StopTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void AddBasketMessaging_ShouldClampStartTimeout_WhenConfiguredTooLow()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "rabbitmq",
                ["RabbitMQ:Port"] = "5672",
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest",
                ["RabbitMQ:StartTimeoutSeconds"] = "0"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddBasketMessaging(configuration, isDevelopment: false);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MassTransitHostOptions>>().Value;

        Assert.That(options.StartTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }
}
