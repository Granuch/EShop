using EShop.Identity.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Identity.UnitTests.Services;

[TestFixture]
public class RevokedTokenCacheTests
{
    private Mock<IDistributedCache> _cacheMock = null!;
    private Mock<ILogger<RevokedTokenCache>> _loggerMock = null!;
    private RevokedTokenCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<RevokedTokenCache>>();
        _cache = new RevokedTokenCache(_cacheMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task IsTokenRevokedAsync_WhenTokenInCache_ReturnsTrue()
    {
        // Arrange
        var token = "test-refresh-token-123";
        var cacheValue = System.Text.Encoding.UTF8.GetBytes("1");

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cacheValue);

        // Act
        var result = await _cache.IsTokenRevokedAsync(token);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsTokenRevokedAsync_WhenTokenNotInCache_ReturnsNull()
    {
        // Arrange
        var token = "test-refresh-token-456";

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        var result = await _cache.IsTokenRevokedAsync(token);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task IsTokenRevokedAsync_WhenEmptyToken_ReturnsTrue()
    {
        // Act
        var result = await _cache.IsTokenRevokedAsync("");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsTokenRevokedAsync_WhenCacheFails_ReturnsNull()
    {
        // Arrange
        var token = "test-token";

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        // Act
        var result = await _cache.IsTokenRevokedAsync(token);

        // Assert - Should return null to indicate fallback to DB
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AddRevokedTokenAsync_WithValidExpiration_AddsToCache()
    {
        // Arrange
        var token = "token-to-revoke";
        var expiresAt = DateTime.UtcNow.AddHours(1);

        // Act
        await _cache.AddRevokedTokenAsync(token, expiresAt);

        // Assert
        _cacheMock.Verify(
            x => x.SetAsync(
                It.Is<string>(k => k.StartsWith("revoked_token:")),
                It.Is<byte[]>(v => System.Text.Encoding.UTF8.GetString(v) == "1"),
                It.Is<DistributedCacheEntryOptions>(o => 
                    o.AbsoluteExpirationRelativeToNow.HasValue &&
                    o.AbsoluteExpirationRelativeToNow.Value > TimeSpan.Zero &&
                    o.AbsoluteExpirationRelativeToNow.Value <= TimeSpan.FromHours(1)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task AddRevokedTokenAsync_WithExpiredToken_DoesNotAddToCache()
    {
        // Arrange
        var token = "already-expired-token";
        var expiresAt = DateTime.UtcNow.AddHours(-1); // Already expired

        // Act
        await _cache.AddRevokedTokenAsync(token, expiresAt);

        // Assert - Should NOT call SetAsync for expired tokens
        _cacheMock.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task AddRevokedTokenAsync_CapsExpiration_At30Days()
    {
        // Arrange
        var token = "long-lived-token";
        var expiresAt = DateTime.UtcNow.AddDays(60); // 60 days from now

        // Act
        await _cache.AddRevokedTokenAsync(token, expiresAt);

        // Assert - TTL should be capped at 30 days
        _cacheMock.Verify(
            x => x.SetAsync(
                It.Is<string>(k => k.StartsWith("revoked_token:")),
                It.IsAny<byte[]>(),
                It.Is<DistributedCacheEntryOptions>(o => 
                    o.AbsoluteExpirationRelativeToNow.HasValue &&
                    o.AbsoluteExpirationRelativeToNow.Value <= TimeSpan.FromDays(30)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RemoveFromRevokedCacheAsync_RemovesCacheEntry()
    {
        // Arrange
        var token = "token-to-remove";

        // Act
        await _cache.RemoveFromRevokedCacheAsync(token);

        // Assert
        _cacheMock.Verify(
            x => x.RemoveAsync(
                It.Is<string>(k => k.StartsWith("revoked_token:")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task AddRevokedTokenAsync_WithEmptyToken_DoesNothing()
    {
        // Act
        await _cache.AddRevokedTokenAsync("", DateTime.UtcNow.AddHours(1));

        // Assert
        _cacheMock.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void CacheKey_UsesSafeHash_NotActualToken()
    {
        // The implementation uses SHA256 hash, so the same token should always produce the same key
        // but the actual token string should never appear in the key
        var token = "sensitive-refresh-token-value";

        // This is a behavioral test - we verify that the cache is called with a hashed key
        _cacheMock
            .Setup(x => x.GetAsync(
                It.Is<string>(k => !k.Contains(token)), // Key should NOT contain the actual token
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        _ = _cache.IsTokenRevokedAsync(token);

        // Verify the cache was called with a key that doesn't expose the actual token
        _cacheMock.Verify(
            x => x.GetAsync(
                It.Is<string>(k => k.StartsWith("revoked_token:") && !k.Contains(token)),
                It.IsAny<CancellationToken>()));
    }
}
