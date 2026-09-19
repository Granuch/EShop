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
    /// Gets a user by their ID <b>with the soft-delete query filter lifted</b>, tracked, so the
    /// caller can mutate and persist it (Admin panel S7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the write-side counterpart of <c>IAdminUserQueryService.GetUserDetailAsync</c> and
    /// exists for exactly one caller: restoring a deleted account. <see cref="GetByIdAsync"/> goes
    /// through <c>UserManager.FindByIdAsync</c>, which runs under <c>!u.IsDeleted</c> and so
    /// answers null for the only users a restore could possibly target — the operation cannot find
    /// its own subject.
    /// </para>
    /// <para>
    /// <b>Do not reach for this anywhere else.</b> Every other write path must keep treating a
    /// deleted account as absent; widening its use is how a deleted user quietly becomes editable,
    /// lockable and loginable again. Note the update itself is unaffected by the filter: EF applies
    /// query filters to queries, not to saving a tracked entity.
    /// </para>
    /// </remarks>
    Task<ApplicationUser?> GetByIdIncludingDeletedAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by their email
    /// </summary>
    Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any account — <b>including a soft-deleted one</b> — already uses this email or the
    /// matching user name, optionally ignoring one account (Admin panel S7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The "including a soft-deleted one" half is the point. <c>UserManager.FindByEmailAsync</c>
    /// runs under the <c>!IsDeleted</c> filter, so it reports a recycled address as free — but the
    /// deleted row still occupies ASP.NET Identity's unique <c>NormalizedUserName</c> index, so the
    /// insert or update then fails with a raw <c>DbUpdateException</c>. Identity registers no
    /// <c>DbUpdate*</c> ProblemDetails branch, so that surfaces as a <b>500</b> where the honest
    /// answer is a 409.
    /// </para>
    /// <para>
    /// Both columns are checked because registration keeps <c>UserName</c> equal to <c>Email</c>
    /// and the unique index is on the user name, not the email.
    /// </para>
    /// </remarks>
    Task<bool> EmailIsTakenAsync(
        string email,
        string? excludingUserId = null,
        CancellationToken cancellationToken = default);
    
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
