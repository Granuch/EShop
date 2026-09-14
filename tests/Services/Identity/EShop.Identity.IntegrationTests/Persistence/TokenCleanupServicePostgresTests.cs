using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Services;
using EShop.Identity.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EShop.Identity.IntegrationTests.Persistence;

/// <summary>
/// TEST-01 / DEBT-07. Covers <see cref="TokenCleanupService"/> against a real PostgreSQL database.
///
/// <para>
/// The cleanup query previously carried this comment: <i>"ExecuteDeleteAsync is more efficient for
/// real databases (PostgreSQL), but we use ToListAsync + RemoveRange for compatibility with
/// InMemory provider in tests"</i> — production behaviour openly dictated by the test provider,
/// loading every expired token into memory in order to delete it. These tests are what allowed
/// that to become a single server-side DELETE.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class TokenCleanupServicePostgresTests
{
    private const int RetentionDays = 7;

    private PostgresDbHarness _harness = null!;
    private string _userId = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _harness = await PostgresDbHarness.CreateAsync();

        await using var context = _harness.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "cleanup@test.com",
            NormalizedUserName = "CLEANUP@TEST.COM",
            Email = "cleanup@test.com",
            NormalizedEmail = "CLEANUP@TEST.COM",
            FirstName = "Cleanup",
            LastName = "Owner",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        _userId = user.Id;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await _harness.DisposeAsync();

    [SetUp]
    public async Task ClearTokensAsync()
    {
        await using var context = _harness.CreateContext();
        await context.RefreshTokens.ExecuteDeleteAsync();
    }

    private async Task<Guid> SeedTokenAsync(DateTime expiresAt, DateTime? revokedAt = null)
    {
        var id = Guid.NewGuid();

        await using var context = _harness.CreateContext();
        context.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = id,
            TokenHash = RefreshTokenHasher.Hash($"token-{id:N}"),
            UserId = _userId,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt
        });

        await context.SaveChangesAsync();
        return id;
    }

    private async Task<int> RunCleanupAsync()
    {
        await using var context = _harness.CreateContext();
        var service = new TokenCleanupService(
            context,
            NullLogger<TokenCleanupService>.Instance,
            Options.Create(new TokenCleanupSettings { RetentionDays = RetentionDays }));

        return await service.CleanupExpiredTokensAsync();
    }

    private async Task<bool> ExistsAsync(Guid id)
    {
        await using var context = _harness.CreateContext();
        return await context.RefreshTokens.AsNoTracking().AnyAsync(t => t.Id == id);
    }

    [Test]
    public async Task Cleanup_DeletesTokensExpiredBeyondTheRetentionWindow()
    {
        var stale = await SeedTokenAsync(expiresAt: DateTime.UtcNow.AddDays(-(RetentionDays + 1)));

        var deleted = await RunCleanupAsync();

        deleted.Should().Be(1);
        (await ExistsAsync(stale)).Should().BeFalse();
    }

    [Test]
    public async Task Cleanup_DeletesTokensRevokedBeyondTheRetentionWindow()
    {
        // Still valid by ExpiresAt, but revoked long ago — the second half of the OR.
        var revoked = await SeedTokenAsync(
            expiresAt: DateTime.UtcNow.AddDays(30),
            revokedAt: DateTime.UtcNow.AddDays(-(RetentionDays + 1)));

        var deleted = await RunCleanupAsync();

        deleted.Should().Be(1);
        (await ExistsAsync(revoked)).Should().BeFalse();
    }

    /// <summary>
    /// The boundary that matters: cleanup must not delete tokens still inside the retention
    /// window, and must not touch live tokens at all. An over-eager DELETE here would log out
    /// every active user, so the negative case deserves its own test.
    /// </summary>
    [Test]
    public async Task Cleanup_LeavesLiveAndRecentlyExpiredTokensAlone()
    {
        var live = await SeedTokenAsync(expiresAt: DateTime.UtcNow.AddDays(7));
        var recentlyExpired = await SeedTokenAsync(expiresAt: DateTime.UtcNow.AddDays(-1));
        var recentlyRevoked = await SeedTokenAsync(
            expiresAt: DateTime.UtcNow.AddDays(30),
            revokedAt: DateTime.UtcNow.AddDays(-1));

        var deleted = await RunCleanupAsync();

        deleted.Should().Be(0);
        (await ExistsAsync(live)).Should().BeTrue();
        (await ExistsAsync(recentlyExpired)).Should().BeTrue();
        (await ExistsAsync(recentlyRevoked)).Should().BeTrue();
    }

    [Test]
    public async Task Cleanup_ReturnsZero_WhenThereIsNothingToDelete()
    {
        var deleted = await RunCleanupAsync();

        deleted.Should().Be(0);
    }

    [Test]
    public async Task Cleanup_DeletesEveryQualifyingRowInOneCall()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedTokenAsync(expiresAt: DateTime.UtcNow.AddDays(-(RetentionDays + 1 + i)));
        }
        var live = await SeedTokenAsync(expiresAt: DateTime.UtcNow.AddDays(7));

        var deleted = await RunCleanupAsync();

        deleted.Should().Be(5);
        (await ExistsAsync(live)).Should().BeTrue();
    }
}
