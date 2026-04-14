using EShop.Basket.Infrastructure.Outbox;
using EShop.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace EShop.Basket.UnitTests.Infrastructure;

[TestFixture]
public class BasketRedisOutboxTests
{
    [Test]
    public void Enqueue_ShouldUseAsyncRedisPush()
    {
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var outbox = new BasketRedisOutbox(
            redis.Object,
            Mock.Of<ILogger<BasketRedisOutbox>>());

        outbox.Enqueue(new ProductCreatedIntegrationEvent
        {
            ProductId = Guid.NewGuid(),
            ProductName = "Name",
            Price = 10m,
            CategoryId = Guid.NewGuid()
        });

        database.Verify(x => x.ListLeftPushAsync(
            BasketOutboxKeys.Pending,
            It.IsAny<RedisValue>(),
            When.Always,
            CommandFlags.None), Times.Once);
    }
}
