using System.Text.Json;
using EShop.Basket.Infrastructure.Configuration;
using EShop.Basket.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace EShop.Basket.UnitTests.Infrastructure;

[TestFixture]
public class RedisBasketRepositoryTests
{
    [Test]
    public async Task GetBasketAsync_WhenDocumentUserDoesNotMatchRequestedUser_ShouldReturnNull()
    {
        var document = new
        {
            userId = "user-2",
            items = Array.Empty<object>(),
            createdAt = DateTime.UtcNow,
            lastModifiedAt = DateTime.UtcNow
        };

        var payload = JsonSerializer.Serialize(document);
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)payload);

        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var repository = new RedisBasketRepository(
            redis.Object,
            Options.Create(new RedisBasketOptions()),
            Mock.Of<ILogger<RedisBasketRepository>>());

        var basket = await repository.GetBasketAsync("user-1", CancellationToken.None);

        Assert.That(basket, Is.Null);
    }

    [Test]
    public async Task GetBasketAsync_WhenDocumentUserMatchesRequestedUser_ShouldReturnBasket()
    {
        var document = new
        {
            userId = "user-1",
            items = new[]
            {
                new { productId = Guid.NewGuid(), productName = "Item", price = 10m, quantity = 2 }
            },
            createdAt = DateTime.UtcNow,
            lastModifiedAt = DateTime.UtcNow
        };

        var payload = JsonSerializer.Serialize(document);
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)payload);

        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var repository = new RedisBasketRepository(
            redis.Object,
            Options.Create(new RedisBasketOptions()),
            Mock.Of<ILogger<RedisBasketRepository>>());

        var basket = await repository.GetBasketAsync("user-1", CancellationToken.None);

        Assert.That(basket, Is.Not.Null);
        Assert.That(basket!.UserId, Is.EqualTo("user-1"));
        Assert.That(basket.Items.Count, Is.EqualTo(1));
    }
}
