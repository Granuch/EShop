using EShop.Identity.Domain.Entities;

namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Repository for user-related operations
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets a user by their ID
    /// </summary>
    Task<ApplicationUser?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a user by their email
    /// </summary>
    Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Records successful-login bookkeeping (<c>LastLoginAt</c>/<c>LastLoginIp</c>) as a single
    /// server-side UPDATE that carries <b>no</b> concurrency token and leaves nothing in the
    /// change tracker.
    ///
    /// <para>
    /// This exists because doing it the obvious way is a live bug. Setting the properties and
    /// calling <c>UserManager.UpdateAsync</c> includes ASP.NET Identity's <c>ConcurrencyStamp</c>
    /// in the WHERE clause, so two logins by the same user race: one wins and the other gets a
    /// failed <c>IdentityResult</c>. <c>LoginCommandHandler</c> already treated that as
    /// non-critical and carried on — but the losing entity stayed tracked as <c>Modified</c> with
    /// a stale stamp, so <c>TransactionBehavior</c>'s commit re-issued the same doomed UPDATE and
    /// the resulting <c>DbUpdateConcurrencyException</c> escaped the handler as a <b>500</b>.
    /// Ten concurrent logins by one user produced one success and nine 500s. EF InMemory has no
    /// concurrency tokens, which is why no test could see it until the suite gained a real
    /// PostgreSQL fixture.
    /// </para>
    ///
    /// <para>
    /// Last-login is telemetry, not state anyone reads back transactionally, so "last writer
    /// wins" is the correct semantic and losing a race must never fail authentication.
    /// </para>
    /// </summary>
    Task UpdateLastLoginAsync(
        string userId,
        DateTime lastLoginAt,
        string? lastLoginIp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft deletes a user
    /// </summary>
    Task DeleteAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles for a user
    /// </summary>
    Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets roles for multiple users in a single database query (prevents N+1 queries)
    /// </summary>
    /// <param name="userIds">Collection of user IDs to get roles for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping user IDs to their role lists</returns>
    Task<IDictionary<string, IReadOnlyList<string>>> GetRolesForUsersAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default);
}
