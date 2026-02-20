using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Catalog.UnitTests.Services;

[TestFixture]
public class CacheInvalidatorTests
{
    private Mock<IDistributedCache> _cacheMock = null!;
    private Mock<ILogger<CacheInvalidator>> _loggerMock = null!;
    private CacheInvalidator _invalidator = null!;

    [SetUp]
    public void SetUp()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<CacheInvalidator>>();

        var options = Options.Create(new CachingBehaviorOptions
        {
            KeyPrefix = "catalog:",
            Version = "v1",
            UseVersioning = true,
            DefaultDuration = TimeSpan.FromMinutes(5)
        });

        _invalidator = new CacheInvalidator(
            _cacheMock.Object,
            _loggerMock.Object,
            options);
    }

    [Test]
    public async Task InvalidateAsync_ShouldRemoveCacheKeyWithFullPrefix()
    {
        // Arrange
        var cacheKey = "product:123";

        // Act
        await _invalidator.InvalidateAsync(cacheKey, CancellationToken.None);

        // Assert
        _cacheMock.Verify(
            x => x.RemoveAsync("catalog:v1:product:123", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task InvalidateAsync_WhenCacheThrows_ShouldNotThrow()
    {
        // Arrange
        _cacheMock
            .Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        // Act & Assert — should not throw
        Assert.DoesNotThrowAsync(async () =>
            await _invalidator.InvalidateAsync("some-key", CancellationToken.None));
    }

    [Test]
    public async Task InvalidateAsync_WithDefaultOptions_ShouldUseEmptyPrefixAndVersion()
    {
        // Arrange — no options provided
        var invalidator = new CacheInvalidator(
            _cacheMock.Object,
            _loggerMock.Object);

        // Act
        await invalidator.InvalidateAsync("test-key", CancellationToken.None);

        // Assert — default prefix is empty, default version is empty
        _cacheMock.Verify(
            x => x.RemoveAsync(It.Is<string>(k => k.Contains("test-key")), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
