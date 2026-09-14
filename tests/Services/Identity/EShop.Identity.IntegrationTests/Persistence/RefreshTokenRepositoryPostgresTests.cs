using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.Infrastructure.Repositories;
using EShop.Identity.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.IntegrationTests.Persistence;

/// <summary>
/// TEST-01 / DEBT-06. Exercises <see cref="RefreshTokenRepository"/> against a real PostgreSQL
/// database.
///
/// <para>
/// These are tests this repo could not previously have had. Both mutating methods below used to
/// branch on <c>_context.Database.IsInMemory()</c>: production ran <c>ExecuteUpdateAsync</c> and
/// every test ran a tracked read-modify loop instead, so the shipped branch had <b>zero</b>
/// coverage. The branches were not equivalent either — <c>ExecuteUpdateAsync</c> issues one
/// server-side UPDATE that bypasses the <c>Version</c> concurrency token, <c>SaveChangesAsync</c>,
/// <c>SetAuditFields()</c> and outbox dispatch, so the tracked loop could pass while the real
/// query was wrong. With this fixture in place the forks were deleted and only the relational path
/// remains.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class RefreshTokenRepositoryPostgresTests
{
    private PostgresDbHarness _harness = null!;
    private string _userId = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _harness = await PostgresDbHarness.CreateAsync();

        // A refresh token row needs a real user: UserId is a required FK with cascade delete.
        await using var context = _harness.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "tokens@test.com",
            NormalizedUserName = "TOKENS@TEST.COM",
            Email = "tokens@test.com",
            NormalizedEmail = "TOKENS@TEST.COM",
            FirstName = "Token",
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

    /// <summary>Writes a token row directly so each test fully controls its starting state.</summary>
    private async Task<string> SeedTokenAsync(
        DateTime? expiresAt = null,
        DateTime? revokedAt = null,
        string? createdByIp = null)
    {
        var plaintext = $"token-{Guid.NewGuid():N}";

        await using var context = _harness.CreateContext();
        context.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = RefreshTokenHasher.Hash(plaintext),
            UserId = _userId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            RevokedAt = revokedAt,
            CreatedByIp = createdByIp
        });

        await context.SaveChangesAsync();
        return plaintext;
    }

    /// <summary>Reads through a fresh context so assertions see the database, not a tracked copy.</summary>
    private async Task<RefreshTokenEntity?> ReadTokenAsync(string plaintext)
    {
        var hash = RefreshTokenHasher.Hash(plaintext);
        await using var context = _harness.CreateContext();
        return await context.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash);
    }

    private async Task<T> WithRepositoryAsync<T>(Func<RefreshTokenRepository, Task<T>> action)
    {
        await using var context = _harness.CreateContext();
        return await action(new RefreshTokenRepository(context));
    }

    [Test]
    public async Task RevokeAllUserTokens_RevokesEveryActiveTokenInOneStatement()
    {
        var first = await SeedTokenAsync();
        var second = await SeedTokenAsync();

        await WithRepositoryAsync(async repository =>
        {
            await repository.RevokeAllUserTokensAsync(_userId, "password changed", "10.0.0.9");
            return true;
        });

        // ExecuteUpdateAsync writes straight to the server, so the rows are persisted without the
        // caller ever calling SaveChangesAsync. That is exactly what the InMemory fork could not
        // reproduce: it mutated tracked entities and depended on the caller committing them.
        var reloadedFirst = await ReadTokenAsync(first);
        var reloadedSecond = await ReadTokenAsync(second);

        reloadedFirst!.RevokedAt.Should().NotBeNull();
        reloadedFirst.RevokeReason.Should().Be("password changed");
        reloadedFirst.RevokedByIp.Should().Be("10.0.0.9");
        reloadedSecond!.RevokedAt.Should().NotBeNull();
    }

    [Test]
    public async Task RevokeAllUserTokens_LeavesAlreadyRevokedTokensUntouched()
    {
        var alreadyRevokedAt = DateTime.UtcNow.AddDays(-1);
        var revoked = await SeedTokenAsync(revokedAt: alreadyRevokedAt);

        await WithRepositoryAsync(async repository =>
        {
            await repository.RevokeAllUserTokensAsync(_userId, "later revocation", "10.0.0.9");
            return true;
        });

        // The WHERE clause filters on RevokedAt == null, so an earlier revocation must keep its
        // original timestamp and reason rather than being overwritten.
        var reloaded = await ReadTokenAsync(revoked);
        reloaded!.RevokedAt.Should().BeCloseTo(alreadyRevokedAt, TimeSpan.FromSeconds(1));
        reloaded.RevokeReason.Should().NotBe("later revocation");
    }

    [Test]
    public async Task RevokeTokenByHashAtomically_ReturnsOneAndRevokes_ForAnActiveToken()
    {
        var plaintext = await SeedTokenAsync();
        var revokedAt = DateTime.UtcNow;

        var affected = await WithRepositoryAsync(repository =>
            repository.RevokeTokenByHashAtomicallyAsync(
                RefreshTokenHasher.Hash(plaintext), revokedAt, "10.0.0.1", null, "rotated"));

        affected.Should().Be(1);

        var reloaded = await ReadTokenAsync(plaintext);
        reloaded!.RevokedAt.Should().NotBeNull();
        reloaded.RevokeReason.Should().Be("rotated");
    }

    /// <summary>
    /// The atomicity guarantee this method exists for. The UPDATE's own WHERE clause is what stops
    /// a token being revoked twice, so a second caller racing the first must get 0 rows rather than
    /// a second successful rotation. A refresh-token replay is exactly this race, which makes the 0
    /// the security-relevant result — and it is only meaningful against a real server-side UPDATE.
    /// </summary>
    [Test]
    public async Task RevokeTokenByHashAtomically_ReturnsZero_WhenTokenIsAlreadyRevoked()
    {
        var plaintext = await SeedTokenAsync();
        var hash = RefreshTokenHasher.Hash(plaintext);

        var first = await WithRepositoryAsync(repository =>
            repository.RevokeTokenByHashAtomicallyAsync(hash, DateTime.UtcNow, "10.0.0.1", null, "rotated"));
        var second = await WithRepositoryAsync(repository =>
            repository.RevokeTokenByHashAtomicallyAsync(hash, DateTime.UtcNow, "10.0.0.2", null, "rotated again"));

        first.Should().Be(1);
        second.Should().Be(0, "a token must only be revocable once — this is what stops replay");
    }

    [Test]
    public async Task RevokeTokenByHashAtomically_ReturnsZero_WhenTokenIsExpired()
    {
        var plaintext = await SeedTokenAsync(expiresAt: DateTime.UtcNow.AddDays(-1));

        var affected = await WithRepositoryAsync(repository =>
            repository.RevokeTokenByHashAtomicallyAsync(
                RefreshTokenHasher.Hash(plaintext), DateTime.UtcNow, "10.0.0.1", null, "rotated"));

        affected.Should().Be(0, "the ExpiresAt > revokedAt predicate must exclude expired tokens");
    }

    [Test]
    public async Task GetByToken_HashesThePlaintextItself_AndFindsTheRow()
    {
        var plaintext = await SeedTokenAsync();

        await using var context = _harness.CreateContext();
        var repository = new RefreshTokenRepository(context);

        var found = await repository.GetByTokenAsync(plaintext);

        found.Should().NotBeNull("the repository hashes the plaintext at this one boundary");
        found!.UserId.Should().Be(_userId);
        found.User.Should().NotBeNull("Include(User) is load-bearing for ValidateRefreshTokenAsync");
    }

    /// <summary>
    /// D-8. <c>CreatedByIp</c>/<c>RevokedByIp</c>/<c>LastLoginIp</c> are <c>varchar(50)</c>
    /// (<c>IdentityDbContext.cs:53,88,89</c>). InMemory ignores length entirely, so nothing had
    /// ever established what the real limit does. The longest realistic address fits comfortably,
    /// so the register's feared <c>22001</c> is not reachable from real traffic.
    /// </summary>
    [Test]
    public async Task IpColumns_AcceptTheLongestRealisticAddress()
    {
        // 45 characters — the documented maximum for an IPv4-mapped IPv6 literal.
        const string longestRealisticIp = "0000:0000:0000:0000:0000:ffff:255.255.255.255";
        longestRealisticIp.Length.Should().BeLessThanOrEqualTo(50);

        var plaintext = await SeedTokenAsync(createdByIp: longestRealisticIp);

        var reloaded = await ReadTokenAsync(plaintext);
        reloaded!.CreatedByIp.Should().Be(longestRealisticIp);
    }

    /// <summary>
    /// The other half of D-8: over the limit PostgreSQL raises
    /// <c>22001 string_data_right_truncation</c> rather than silently truncating. Worth pinning
    /// because the failure mode is provider-specific — InMemory stores an oversized value happily,
    /// so this can only ever be caught here.
    /// </summary>
    [Test]
    public async Task IpColumns_RejectAnOversizedValueRatherThanTruncating()
    {
        var oversized = new string('a', 51);

        var act = async () => await SeedTokenAsync(createdByIp: oversized);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
