using EShop.ApiGateway.IntegrationTests.Fixtures;
using EShop.ApiGateway.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace EShop.ApiGateway.IntegrationTests.Routes;

/// <summary>
/// Every gateway route must sit behind one of the <c>*ProxyGuardMiddleware</c> classes.
///
/// <para>
/// The guards match hardcoded path-prefix arrays, so a route added on a path none of them covers
/// keeps working perfectly while silently losing two things: the request-body size cap, and the
/// rewrite that turns an upstream 502 into a 503 with <c>Retry-After</c>. Nothing fails, nothing
/// logs, and the only symptom is a bare 502 reaching a client during an outage. That is the whole
/// reason this test exists — it is the half of the admin-route work that is easiest to forget,
/// because the route itself works.
/// </para>
///
/// <para>
/// <b>Payment is a known, pre-existing hole</b> and is listed as such below rather than silently
/// skipped: <c>/api/v1/payments</c> has never had a guard. Closing it is its own change; recording
/// it here means the gap is visible instead of implied.
/// </para>
/// </summary>
[TestFixture]
public sealed class ProxyGuardCoverageTests
{
    /// <summary>Route path prefixes knowingly served without a proxy guard.</summary>
    private static readonly string[] KnownUnguardedPrefixes =
    [
        "/api/v1/payments"
    ];

    private static IEnumerable<string> GuardedPrefixes =>
        IdentityProxyGuardMiddleware.IdentityPathPrefixes
            .Concat(CatalogProxyGuardMiddleware.CatalogPathPrefixes)
            .Concat(OrderingProxyGuardMiddleware.OrderingPathPrefixes)
            .Append(BasketProxyGuardMiddleware.BasketPathPrefix);

    [Test]
    public void EveryApiRoute_SitsBehindAProxyGuard_OrIsAKnownHole()
    {
        using var factory = new RouteAuthorizationApiFactory();

        var routePaths = factory.Services.GetRequiredService<IProxyConfigProvider>()
            .GetConfig().Routes
            .Select(r => r.Match.Path)
            .Where(p => p is not null && p.StartsWith("/api/", StringComparison.Ordinal))
            .Select(p => p!)
            .Distinct(StringComparer.Ordinal);

        var covered = GuardedPrefixes.Concat(KnownUnguardedPrefixes).ToArray();

        var uncovered = routePaths
            .Where(path => !covered.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.That(uncovered, Is.Empty,
            "these gateway routes match no proxy guard, so they have no request-body cap and leak raw 502s: "
            + string.Join(", ", uncovered)
            + ". Add the path prefix to the relevant *ProxyGuardMiddleware in the same change as the route.");
    }

    [Test]
    public void TheKnownHoles_AreStillHoles()
    {
        // Paired with the test above so the allowance cannot outlive the gap it excuses: add a
        // Payment guard and this test tells you to delete the entry rather than leaving a list that
        // quietly grants exemptions nobody needs any more.
        foreach (var prefix in KnownUnguardedPrefixes)
        {
            Assert.That(
                GuardedPrefixes.Any(guarded => prefix.StartsWith(guarded, StringComparison.OrdinalIgnoreCase)),
                Is.False,
                $"'{prefix}' is now covered by a proxy guard — remove it from KnownUnguardedPrefixes");
        }
    }
}
