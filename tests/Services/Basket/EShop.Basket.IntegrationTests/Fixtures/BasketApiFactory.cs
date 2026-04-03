using EShop.Basket.Infrastructure.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Fixtures;

public class BasketApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var hostedOutboxDescriptor = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && d.ImplementationType == typeof(BasketRedisOutboxProcessorService))
                .ToList();

            foreach (var descriptor in hostedOutboxDescriptor)
            {
                services.Remove(descriptor);
            }

            var redisDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (redisDescriptor != null)
            {
                services.Remove(redisDescriptor);
            }

            var databaseMock = new Mock<IDatabase>();
            databaseMock
                .Setup(db => db.PingAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(TimeSpan.FromMilliseconds(1));
            databaseMock
                .Setup(db => db.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(0);

            var redisMock = new Mock<IConnectionMultiplexer>();
            redisMock
                .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(databaseMock.Object);

            services.AddSingleton(redisMock.Object);
        });
    }
}
