using System.Text.Json;
using EShop.Basket.Domain.Entities;
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

    [Test]
    public async Task SaveBasketAsync_ShouldRefreshReverseIndexTtlOutsideTransaction()
    {
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(Guid.NewGuid(), "Item A", 10m, 1);
        basket.AddItem(Guid.NewGuid(), "Item B", 20m, 1);

        var transaction = new Mock<ITransaction>();
        transaction
            .Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        transaction
            .Setup(x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        transaction
            .Setup(x => x.SetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        transaction
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        database
            .Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(transaction.Object);
        database
            .Setup(x => x.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var options = Options.Create(new RedisBasketOptions
        {
            BasketTtl = TimeSpan.FromMinutes(30)
        });

        var repository = new RedisBasketRepository(
            redis.Object,
            options,
            Mock.Of<ILogger<RedisBasketRepository>>());

        await repository.SaveBasketAsync(basket, CancellationToken.None);

        transaction.Verify(
            x => x.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()),
            Times.Never);
        database.Verify(
            x => x.KeyExpireAsync(It.IsAny<RedisKey>(), options.Value.BasketTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Exactly(2));
    }
}
