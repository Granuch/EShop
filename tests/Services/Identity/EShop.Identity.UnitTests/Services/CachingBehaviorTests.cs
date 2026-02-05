using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Application;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;

namespace EShop.Identity.UnitTests.Services;

[TestFixture]
public class CachingBehaviorTests
{
    private Mock<IDistributedCache> _cacheMock = null!;
    private Mock<ILogger<CachingBehavior<TestCacheableQuery, TestResponse>>> _loggerMock = null!;
    private CachingBehavior<TestCacheableQuery, TestResponse> _behavior = null!;
    private Mock<RequestHandlerDelegate<TestResponse>> _nextMock = null!;

    [SetUp]
    public void SetUp()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<CachingBehavior<TestCacheableQuery, TestResponse>>>();
        
        var options = Options.Create(new CachingBehaviorOptions
        {
            KeyPrefix = "test:",
            DefaultDuration = TimeSpan.FromMinutes(5),
            UseVersioning = true,
            Version = "v1"
        });

        _behavior = new CachingBehavior<TestCacheableQuery, TestResponse>(
            _cacheMock.Object,
            _loggerMock.Object,
            options);

        _nextMock = new Mock<RequestHandlerDelegate<TestResponse>>();
    }

    [Test]
    public async Task Handle_WhenCacheHit_ReturnsFromCacheWithoutCallingHandler()
    {
        // Arrange
        var query = new TestCacheableQuery { UserId = "user-1" };
        var cachedResponse = new TestResponse { Data = "cached-data" };
        var serializedResponse = JsonSerializer.SerializeToUtf8Bytes(cachedResponse, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // Setup cache to return serialized response for any key (since versioning adds complexity)
        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(serializedResponse);

        // Act
        var result = await _behavior.Handle(query, _nextMock.Object, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Data, Is.EqualTo("cached-data"));
        _nextMock.Verify(x => x(), Times.Never);
    }

    [Test]
    public async Task Handle_WhenCacheMiss_CallsHandlerAndCachesResult()
    {
        // Arrange
        var query = new TestCacheableQuery { UserId = "user-2" };
        var handlerResponse = new TestResponse { Data = "handler-data" };

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _nextMock.Setup(x => x()).ReturnsAsync(handlerResponse);

        // Act
        var result = await _behavior.Handle(query, _nextMock.Object, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Data, Is.EqualTo("handler-data"));
        
        _nextMock.Verify(x => x(), Times.Once);
        _cacheMock.Verify(
            x => x.SetAsync(
                It.Is<string>(k => k.Contains("test-cache-key:user-2")),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_WhenNonCacheableQuery_CallsHandlerDirectly()
    {
        // Arrange
        var nonCacheableLoggerMock = new Mock<ILogger<CachingBehavior<TestNonCacheableQuery, TestResponse>>>();
        var nonCacheableBehavior = new CachingBehavior<TestNonCacheableQuery, TestResponse>(
            _cacheMock.Object,
            nonCacheableLoggerMock.Object);

        var query = new TestNonCacheableQuery();
        var handlerResponse = new TestResponse { Data = "direct-response" };

        var nextMock = new Mock<RequestHandlerDelegate<TestResponse>>();
        nextMock.Setup(x => x()).ReturnsAsync(handlerResponse);

        // Act
        var result = await nonCacheableBehavior.Handle(query, nextMock.Object, CancellationToken.None);

        // Assert
        Assert.That(result.Data, Is.EqualTo("direct-response"));
        nextMock.Verify(x => x(), Times.Once);
        
        // Cache should never be accessed for non-cacheable queries
        _cacheMock.Verify(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _cacheMock.Verify(
            x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task Handle_WhenCacheThrows_StillExecutesHandler()
    {
        // Arrange
        var query = new TestCacheableQuery { UserId = "user-3" };
        var handlerResponse = new TestResponse { Data = "fallback-data" };

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        _nextMock.Setup(x => x()).ReturnsAsync(handlerResponse);

        // Act
        var result = await _behavior.Handle(query, _nextMock.Object, CancellationToken.None);

        // Assert - Should succeed despite cache failure
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Data, Is.EqualTo("fallback-data"));
        _nextMock.Verify(x => x(), Times.Once);
    }

    [Test]
    public async Task Handle_WhenHandlerReturnsNull_DoesNotCache()
    {
        // Arrange
        var query = new TestCacheableQuery { UserId = "user-4" };

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _nextMock.Setup(x => x()).ReturnsAsync((TestResponse?)null!);

        // Act
        var result = await _behavior.Handle(query, _nextMock.Object, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);
        _cacheMock.Verify(
            x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task Handle_UsesVersionedCacheKey()
    {
        // Arrange
        var query = new TestCacheableQuery { UserId = "user-5" };
        string capturedCacheKey = string.Empty;

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => capturedCacheKey = key)
            .ReturnsAsync((byte[]?)null);

        _nextMock.Setup(x => x()).ReturnsAsync(new TestResponse { Data = "data" });

        // Act
        await _behavior.Handle(query, _nextMock.Object, CancellationToken.None);

        // Assert - Cache key should include version
        Assert.That(capturedCacheKey, Does.Contain("v1"));
        Assert.That(capturedCacheKey, Does.StartWith("test:"));
    }

    // Test classes
    public class TestCacheableQuery : IRequest<TestResponse>, ICacheableQuery
    {
        public string UserId { get; init; } = string.Empty;
        public string CacheKey => $"test-cache-key:{UserId}";
        public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
        public TimeSpan? SlidingExpiration => null;
    }

    public class TestNonCacheableQuery : IRequest<TestResponse>
    {
    }

    public class TestResponse
    {
        public string Data { get; init; } = string.Empty;
    }
}
