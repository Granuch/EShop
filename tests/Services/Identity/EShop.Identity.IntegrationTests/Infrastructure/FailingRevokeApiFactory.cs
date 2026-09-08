using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Infrastructure.Repositories;
using EShop.Identity.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

// This file's own namespace is ...IntegrationTests.Infrastructure, which shadows the service's
// EShop.Identity.Infrastructure.* namespaces for unqualified lookups. Aliasing the DbContext
// avoids CS0234 without renaming the folder.
using IdentityDbContext = EShop.Identity.Infrastructure.Data.IdentityDbContext;

namespace EShop.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// SEC-06. A host whose "revoke every refresh token" step always fails, so a test can prove what
/// happens to the password change that asked for it.
///
/// <para>
/// Everything except <see cref="IRefreshTokenRepository.RevokeAllUserTokensAsync"/> is delegated
/// to the real repository — login has to keep working, which means token issuance has to keep
/// working, so this is a decorator rather than a stub.
/// </para>
/// </summary>
public class FailingRevokeApiFactory : PostgresIdentityApiFactory
{
    public const string FailureMessage = "simulated revoke failure";

    private FailingRevokeApiFactory(string connectionString) : base(connectionString)
    {
    }

    public static async Task<FailingRevokeApiFactory> CreateAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync(cancellationToken);
        return new FailingRevokeApiFactory(connectionString);
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        services.AddScoped<IRefreshTokenRepository>(provider =>
            new ThrowsOnRevokeAll(
                new RefreshTokenRepository(provider.GetRequiredService<IdentityDbContext>())));
    }

    private sealed class ThrowsOnRevokeAll(IRefreshTokenRepository inner) : IRefreshTokenRepository
    {
        public Task<RefreshTokenEntity?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
            => inner.GetByTokenAsync(token, cancellationToken);

        public Task<IEnumerable<RefreshTokenEntity>> GetActiveTokensByUserIdAsync(
            string userId, CancellationToken cancellationToken = default)
            => inner.GetActiveTokensByUserIdAsync(userId, cancellationToken);

        public Task AddAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default)
            => inner.AddAsync(refreshToken, cancellationToken);

        public Task UpdateAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default)
            => inner.UpdateAsync(refreshToken, cancellationToken);

        /// <summary>The one method under test: it must throw, not return quietly.</summary>
        public Task RevokeAllUserTokensAsync(
            string userId, string? reason = null, string? ipAddress = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(FailureMessage);

        public Task<int> RevokeTokenByHashAtomicallyAsync(
            string tokenHash,
            DateTime revokedAt,
            string? revokedByIp,
            string? replacedByTokenHash,
            string revokeReason,
            CancellationToken cancellationToken = default)
            => inner.RevokeTokenByHashAtomicallyAsync(
                tokenHash, revokedAt, revokedByIp, replacedByTokenHash, revokeReason, cancellationToken);
    }
}
