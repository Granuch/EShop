using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Security;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace EShop.Identity.UnitTests.Security;

[TestFixture]
public class LoginAttemptTrackerTests
{
    [Test]
    public async Task RecordFailedAttemptAsync_WithRedisMultiplexer_ShouldUseAtomicIncrement()
    {
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        database
            .Setup(x => x.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        database
            .Setup(x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var tracker = new LoginAttemptTracker(
            cache.Object,
            Options.Create(new BruteForceProtectionSettings()),
            Mock.Of<ILogger<LoginAttemptTracker>>(),
            redis.Object);

        await tracker.RecordFailedAttemptAsync("user@test.com", "127.0.0.1", CancellationToken.None);

        database.Verify(
            x => x.StringIncrementAsync(It.IsAny<RedisKey>(), 1, It.IsAny<CommandFlags>()),
            Times.Exactly(2));
        database.Verify(
            x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Test]
    public async Task GetFailedAttemptCountAsync_WithRedisMultiplexer_ShouldReadFromRedis()
    {
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"3");

        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var tracker = new LoginAttemptTracker(
            cache.Object,
            Options.Create(new BruteForceProtectionSettings()),
            Mock.Of<ILogger<LoginAttemptTracker>>(),
            redis.Object);

        var count = await tracker.GetFailedAttemptCountAsync("user@test.com", CancellationToken.None);

        Assert.That(count, Is.EqualTo(3));
    }
}
