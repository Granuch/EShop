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
}
