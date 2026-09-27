namespace EShop.BuildingBlocks.Infrastructure.Authorization;

/// <summary>
/// The permission vocabulary every admin-facing endpoint authorizes against (decision Q4c).
///
/// <para>
/// A permission names <b>what may be done</b>, not <b>who may do it</b>. That separation is the
/// whole point: <c>RequireRole("Admin")</c> spread across six components means introducing a
/// second operator class later is a change to every endpoint, whereas a permission is a change to
/// <see cref="RolePermissionBundles"/> alone.
/// </para>
///
/// <para>
/// <b>The string is the policy name.</b> <c>RequireAuthorization(EShopPermissions.CatalogWrite)</c>
/// resolves the policy that <c>AddEShopPermissionPolicies()</c> registered under the same string,
/// so there is no second table mapping one to the other and nothing to keep in sync. A typo
/// therefore fails loudly at request time with "The AuthorizationPolicy named ... was not found"
/// rather than silently authorizing — but see <c>PermissionPolicyTests</c>, which asserts every
/// constant here is registered, so a typo fails at build time instead.
/// </para>
/// </summary>
public static class EShopPermissions
{
    /// <summary>The claim type carrying a permission directly, for a future per-user grant.</summary>
    /// <remarks>
    /// Nothing issues this claim today — permissions are resolved from the caller's roles through
    /// <see cref="RolePermissionBundles"/>. It is honoured from the start so that granting a
    /// permission to one user later is an Identity-only change and touches no endpoint and no
    /// policy.
    /// </remarks>
    public const string ClaimType = "permission";

    // Catalog
    /// <summary>Read catalog data the public cannot see: drafts, soft-deleted products, stock reports.</summary>
    public const string CatalogRead = "catalog.read";

    /// <summary>Create, change, publish, delete and restore products and categories.</summary>
    public const string CatalogWrite = "catalog.write";

    // Ordering
    /// <summary>List and read any order, not only one's own.</summary>
    public const string OrdersRead = "orders.read";

    /// <summary>Drive an order's lifecycle: ship, deliver, edit lines, edit the shipping address, add notes.</summary>
    public const string OrdersWrite = "orders.write";

    // Payment
    /// <summary>List and read any payment, and its event history.</summary>
    public const string PaymentsRead = "payments.read";

    /// <summary>Record a payment, including an offline one (decision Q6a).</summary>
    public const string PaymentsWrite = "payments.write";

    /// <summary>
    /// Refund a payment. Deliberately separate from <see cref="PaymentsWrite"/>: refunding moves
    /// real money outward and is the one action most worth withholding from a junior operator.
    /// </summary>
    public const string PaymentsRefund = "payments.refund";

    // Identity
    /// <summary>List and read user accounts, their roles and their live sessions.</summary>
    public const string UsersRead = "users.read";

    /// <summary>Create, edit, lock, deactivate, delete and restore user accounts.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>Create and delete roles, and change who is in them.</summary>
    public const string RolesManage = "roles.manage";

    // Notification
    /// <summary>Read the notification delivery journal.</summary>
    public const string NotificationsRead = "notifications.read";

    /// <summary>Resend, retry and close notifications, and send template test messages.</summary>
    public const string NotificationsManage = "notifications.manage";

    // Basket
    /// <summary>Read another user's basket. There is deliberately no basket write permission (decision Q7a).</summary>
    public const string BasketsRead = "baskets.read";

    // Cross-cutting
    /// <summary>Read the admin audit trail.</summary>
    public const string AuditRead = "audit.read";

    /// <summary>
    /// Operate the platform: aggregate health, cache invalidation, feature flags, outbox
    /// dead-letter replay, and the read-only settings projection.
    /// </summary>
    public const string SystemManage = "system.manage";

    /// <summary>
    /// Every permission. The registration source for <c>AddEShopPermissionPolicies()</c> and the
    /// definition of the <c>Admin</c> bundle, so a permission added above cannot be forgotten in
    /// either place.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        CatalogRead,
        CatalogWrite,
        OrdersRead,
        OrdersWrite,
        PaymentsRead,
        PaymentsWrite,
        PaymentsRefund,
        UsersRead,
        UsersManage,
        RolesManage,
        NotificationsRead,
        NotificationsManage,
        BasketsRead,
        AuditRead,
        SystemManage
    ];
}
