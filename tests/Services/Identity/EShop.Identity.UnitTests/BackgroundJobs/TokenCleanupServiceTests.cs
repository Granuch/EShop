using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.BackgroundJobs;

[TestFixture]
public class TokenCleanupServiceTests
{
    private IdentityDbContext _context = null!;
    private Mock<ILogger<TokenCleanupService>> _loggerMock = null!;
    private IOptions<TokenCleanupSettings> _settings = null!;
    private TokenCleanupService _cleanupService = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new IdentityDbContext(options);
        _loggerMock = new Mock<ILogger<TokenCleanupService>>();

        _settings = Options.Create(new TokenCleanupSettings
        {
            Enabled = true,
            CleanupIntervalHours = 24,
            RetentionDays = 30,
            InitialDelayMinutes = 1
        });

        _cleanupService = new TokenCleanupService(_context, _loggerMock.Object, _settings);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task CleanupExpiredTokens_DeletesExpiredTokensOlderThan30Days()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        
        // Token expired 31 days ago - should be deleted
        var oldExpiredToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "old-expired-token",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-35),
            ExpiresAt = DateTime.UtcNow.AddDays(-31)
        };

        // Token expired 1 day ago - should NOT be deleted (within 30-day retention)
        var recentExpiredToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "recent-expired-token",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        };

        // Active token - should NOT be deleted
        var activeToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "active-token",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        await _context.RefreshTokens.AddRangeAsync(oldExpiredToken, recentExpiredToken, activeToken);
        await _context.SaveChangesAsync();

        // Act
        var deletedCount = await _cleanupService.CleanupExpiredTokensAsync();

        // Assert
        Assert.That(deletedCount, Is.EqualTo(1), "Should delete exactly 1 old expired token");

        var remainingTokens = await _context.RefreshTokens.ToListAsync();
        Assert.That(remainingTokens, Has.Count.EqualTo(2), "Should have 2 remaining tokens");
        Assert.That(remainingTokens.Any(t => t.Token == "old-expired-token"), Is.False, "Old expired token should be deleted");
        Assert.That(remainingTokens.Any(t => t.Token == "recent-expired-token"), Is.True, "Recent expired token should remain");
        Assert.That(remainingTokens.Any(t => t.Token == "active-token"), Is.True, "Active token should remain");
    }

    [Test]
    public async Task CleanupExpiredTokens_DeletesRevokedTokensOlderThan30Days()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        
        // Token revoked 31 days ago - should be deleted
        var oldRevokedToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "old-revoked-token",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-35),
            ExpiresAt = DateTime.UtcNow.AddDays(7), // Not yet expired
            RevokedAt = DateTime.UtcNow.AddDays(-31),
            RevokedByIp = "127.0.0.1",
            RevokeReason = "User logout"
        };

        // Token revoked 1 day ago - should NOT be deleted (within 30-day retention)
        var recentRevokedToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "recent-revoked-token",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow.AddDays(-1),
            RevokedByIp = "127.0.0.1",
            RevokeReason = "User logout"
        };

        await _context.RefreshTokens.AddRangeAsync(oldRevokedToken, recentRevokedToken);
        await _context.SaveChangesAsync();

        // Act
        var deletedCount = await _cleanupService.CleanupExpiredTokensAsync();

        // Assert
        Assert.That(deletedCount, Is.EqualTo(1), "Should delete exactly 1 old revoked token");

        var remainingTokens = await _context.RefreshTokens.ToListAsync();
        Assert.That(remainingTokens, Has.Count.EqualTo(1), "Should have 1 remaining token");
        Assert.That(remainingTokens.Any(t => t.Token == "old-revoked-token"), Is.False, "Old revoked token should be deleted");
        Assert.That(remainingTokens.Any(t => t.Token == "recent-revoked-token"), Is.True, "Recent revoked token should remain");
    }

    [Test]
    public async Task CleanupExpiredTokens_HandlesEmptyDatabase()
    {
        // Act
        var deletedCount = await _cleanupService.CleanupExpiredTokensAsync();

        // Assert
        Assert.That(deletedCount, Is.EqualTo(0), "Should delete 0 tokens from empty database");

        var tokens = await _context.RefreshTokens.ToListAsync();
        Assert.That(tokens, Is.Empty, "Database should remain empty");
    }

    [Test]
    public async Task CleanupExpiredTokens_RespectsRetentionPeriod()
    {
        // Arrange - Use custom retention period of 7 days
        var customSettings = Options.Create(new TokenCleanupSettings
        {
            RetentionDays = 7
        });
        var customCleanupService = new TokenCleanupService(_context, _loggerMock.Object, customSettings);

        var userId = Guid.NewGuid().ToString();

        // Token expired 8 days ago - should be deleted with 7-day retention
        var token8DaysOld = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "8-days-old",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-8)
        };

        // Token expired 6 days ago - should NOT be deleted with 7-day retention
        var token6DaysOld = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "6-days-old",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-8),
            ExpiresAt = DateTime.UtcNow.AddDays(-6)
        };

        await _context.RefreshTokens.AddRangeAsync(token8DaysOld, token6DaysOld);
        await _context.SaveChangesAsync();

        // Act
        var deletedCount = await customCleanupService.CleanupExpiredTokensAsync();

        // Assert
        Assert.That(deletedCount, Is.EqualTo(1), "Should delete exactly 1 token older than 7 days");

        var remainingTokens = await _context.RefreshTokens.ToListAsync();
        Assert.That(remainingTokens, Has.Count.EqualTo(1));
        Assert.That(remainingTokens[0].Token, Is.EqualTo("6-days-old"));
    }

    [Test]
    public async Task CleanupExpiredTokens_DeletesBothExpiredAndRevokedTokens()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();

        var oldExpiredToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "old-expired",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-35),
            ExpiresAt = DateTime.UtcNow.AddDays(-31)
        };

        var oldRevokedToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "old-revoked",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-35),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow.AddDays(-31)
        };

        var recentToken = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "recent",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        await _context.RefreshTokens.AddRangeAsync(oldExpiredToken, oldRevokedToken, recentToken);
        await _context.SaveChangesAsync();

        // Act
        var deletedCount = await _cleanupService.CleanupExpiredTokensAsync();

        // Assert
        Assert.That(deletedCount, Is.EqualTo(2), "Should delete both old expired and old revoked tokens");

        var remainingTokens = await _context.RefreshTokens.ToListAsync();
        Assert.That(remainingTokens, Has.Count.EqualTo(1));
        Assert.That(remainingTokens[0].Token, Is.EqualTo("recent"));
    }

    [Test]
    public async Task CleanupExpiredTokens_ReturnsCorrectDeletedCount()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var oldTokens = Enumerable.Range(1, 5).Select(i => new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = $"old-token-{i}",
            UserId = userId,
            CreatedAt = DateTime.UtcNow.AddDays(-35),
            ExpiresAt = DateTime.UtcNow.AddDays(-31)
        }).ToList();

        await _context.RefreshTokens.AddRangeAsync(oldTokens);
        await _context.SaveChangesAsync();

        // Act
        var deletedCount = await _cleanupService.CleanupExpiredTokensAsync();

        // Assert
        Assert.That(deletedCount, Is.EqualTo(5), "Should return correct count of deleted tokens");
        Assert.That(await _context.RefreshTokens.CountAsync(), Is.EqualTo(0), "All tokens should be deleted");
    }
}
