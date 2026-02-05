using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Identity.UnitTests.Services;

[TestFixture]
public class CachedUserRolesServiceTests
{
    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<IDistributedCache> _cacheMock = null!;
    private Mock<ILogger<CachedUserRolesService>> _loggerMock = null!;
    private CachedUserRolesService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // UserManager mock setup
        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<CachedUserRolesService>>();

        _service = new CachedUserRolesService(
            _userManagerMock.Object,
            _cacheMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task GetRolesAsync_WhenCacheHit_ReturnsFromCache()
    {
        // Arrange
        var user = CreateTestUser("user-1");
        var cachedRoles = new List<string> { "Admin", "User" };
        var serializedRoles = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(cachedRoles);

        _cacheMock
            .Setup(x => x.GetAsync("user_roles:user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(serializedRoles);

        // Act
        var result = await _service.GetRolesAsync(user);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("Admin"));
        Assert.That(result, Contains.Item("User"));

        // Verify UserManager was NOT called (cache hit)
        _userManagerMock.Verify(
            x => x.GetRolesAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
    }

    [Test]
    public async Task GetRolesAsync_WhenCacheMiss_FetchesFromDbAndCaches()
    {
        // Arrange
        var user = CreateTestUser("user-2");
        var dbRoles = new List<string> { "Editor" };

        // Cache miss - returns null
        _cacheMock
            .Setup(x => x.GetAsync("user_roles:user-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _userManagerMock
            .Setup(x => x.FindByIdAsync("user-2"))
            .ReturnsAsync(user);

        _userManagerMock
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(dbRoles);

        // Act
        var result = await _service.GetRolesByUserIdAsync("user-2");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result, Contains.Item("Editor"));

        // Verify cache was populated
        _cacheMock.Verify(
            x => x.SetAsync(
                "user_roles:user-2",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task GetRolesByUserIdAsync_WhenUserNotFound_ReturnsEmptyList()
    {
        // Arrange
        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _userManagerMock
            .Setup(x => x.FindByIdAsync("nonexistent"))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _service.GetRolesByUserIdAsync("nonexistent");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task InvalidateRolesCacheAsync_RemovesCacheEntry()
    {
        // Act
        await _service.InvalidateRolesCacheAsync("user-3");

        // Assert
        _cacheMock.Verify(
            x => x.RemoveAsync("user_roles:user-3", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task GetRolesAsync_WhenCacheFails_FallsBackToDatabase()
    {
        // Arrange
        var user = CreateTestUser("user-4");
        var dbRoles = new List<string> { "Fallback" };

        // Cache throws exception
        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        _userManagerMock
            .Setup(x => x.FindByIdAsync("user-4"))
            .ReturnsAsync(user);

        _userManagerMock
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(dbRoles);

        // Act
        var result = await _service.GetRolesByUserIdAsync("user-4");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result, Contains.Item("Fallback"));
    }

    private static ApplicationUser CreateTestUser(string userId) => new()
    {
        Id = userId,
        Email = $"{userId}@test.com",
        UserName = $"{userId}@test.com",
        FirstName = "Test",
        LastName = "User"
    };
}
