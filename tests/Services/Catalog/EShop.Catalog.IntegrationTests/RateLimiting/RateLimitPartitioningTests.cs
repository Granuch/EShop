using System.Net;
using EShop.Catalog.IntegrationTests.Fixtures;
using FluentAssertions;

namespace EShop.Catalog.IntegrationTests.RateLimiting;

/// <summary>
/// H3. The rate limiters exist to stop one caller exhausting the service; behind the gateway they
/// did the opposite.
///
/// <para>
/// <c>docker-compose.yml</c> set no <c>ForwardedHeaders__KnownProxies</c> or
/// <c>KnownNetworks</c> for any service, so <c>AddEShopForwardedHeaders</c> returned false, the
/// middleware was never registered, and <c>RemoteIpAddress</c> was the gateway's bridge address for
/// every request. Both limiters therefore collapsed to a <b>single bucket shared by the entire user
/// base</b> — one noisy client 429s everyone. Nothing failed and nothing logged; the limiter code
/// itself was correct and had nothing to partition on.
/// </para>
///
/// <para>
/// These tests exercise the whole chain: peer address → <c>UseForwardedHeaders</c> →
/// <c>GetClientPartitionKey</c> → bucket.
/// </para>
///
/// <para>
/// <b>Note which endpoint exercises which limiter.</b> <c>GET /api/v1/products</c> carries
/// <c>RequireRateLimiting("search")</c> <i>on top of</i> the global limiter, so the tighter of the
/// two wins there and a test aimed at the global limiter would be rejected early by the search one.
/// <c>GET /api/v1/categories</c> has only the global limiter, so it is the endpoint that isolates
/// it.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class RateLimitPartitioningTests : IntegrationTestBase
{
    private const string GlobalOnlyEndpoint = "/api/v1/categories";
    private const string SearchLimitedEndpoint = "/api/v1/products?PageNumber=1&PageSize=1";

    /// <summary>
    /// Rate-limit buckets live in the host, so a fixture-scoped host would let one test spend
    /// another's allowance and make the fixture order-dependent. Same reason Identity's
    /// <c>RateLimitingTests</c> opts out.
    /// </summary>
    protected override bool UseFixtureScopedHost => false;

    protected override async Task<CatalogApiFactory> CreateFactoryAsync()
        => await RateLimitingApiFactory.CreateAsync();

    private async Task<HttpStatusCode> GetAsAsync(string clientIp, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        // The simulated gateway is the TCP peer; the real client is named in X-Forwarded-For,
        // exactly as it arrives in the deployed stack.
        request.Headers.Add(RateLimitingApiFactory.RemoteIpHeader, RateLimitingApiFactory.TrustedProxyIp);
        request.Headers.Add("X-Forwarded-For", clientIp);

        using var response = await Client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>
    /// The defect itself: one client exhausting its allowance must not affect anyone else. Before
    /// the compose fix both clients landed in the gateway's single bucket, so the second client's
    /// very first request was already 429.
    /// </summary>
    [Test]
    public async Task OneClientExhaustingItsBudget_DoesNotThrottleAnother()
    {
        const string noisy = "203.0.113.10";
        const string quiet = "203.0.113.20";

        var noisyStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < RateLimitingApiFactory.GlobalPermitLimit + 2; i++)
        {
            noisyStatuses.Add(await GetAsAsync(noisy, GlobalOnlyEndpoint));
        }

        noisyStatuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "the noisy client must exhaust its own allowance");

        (await GetAsAsync(quiet, GlobalOnlyEndpoint)).Should().NotBe(HttpStatusCode.TooManyRequests,
            "a second client has its own bucket — sharing one is the H3 defect, where a single "
            + "caller could 429 the entire user base");
    }

    /// <summary>
    /// The forwarded header must actually be honoured. If <c>KnownProxies</c> were unset — the
    /// deployed state before this stage — every request would partition on the gateway's peer
    /// address and these two clients would share a bucket.
    /// </summary>
    [Test]
    public async Task DistinctForwardedClients_GetDistinctBuckets()
    {
        for (var i = 0; i < RateLimitingApiFactory.GlobalPermitLimit; i++)
        {
            (await GetAsAsync("198.51.100.1", GlobalOnlyEndpoint))
                .Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        // A different forwarded client, same TCP peer.
        (await GetAsAsync("198.51.100.2", GlobalOnlyEndpoint))
            .Should().NotBe(HttpStatusCode.TooManyRequests,
                "the partition key must come from X-Forwarded-For, not from the gateway's peer address");
    }

    /// <summary>
    /// A dual-stack client must not get two allowances. This is what the raw
    /// <c>RemoteIpAddress?.ToString()</c> partition key that Catalog's and Basket's global limiters
    /// used allowed: <c>::ffff:203.0.113.30</c> and <c>203.0.113.30</c> are the same host and were
    /// getting a bucket each.
    /// </summary>
    [Test]
    public async Task Ipv4MappedIpv6AndPlainIpv4_ShareOneBucket()
    {
        const string plain = "203.0.113.30";
        const string mapped = "::ffff:203.0.113.30";

        for (var i = 0; i < RateLimitingApiFactory.GlobalPermitLimit; i++)
        {
            await GetAsAsync(plain, GlobalOnlyEndpoint);
        }

        var responses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            responses.Add(await GetAsAsync(mapped, GlobalOnlyEndpoint));
        }

        responses.Should().Contain(HttpStatusCode.TooManyRequests,
            "the same host reached over a dual-stack socket must not double its allowance");
    }

    /// <summary>
    /// The named <c>"search"</c> policy is partitioned too. It was declared with
    /// <c>AddFixedWindowLimiter(name, …)</c> until Stage 2 of the earlier work — a form with no
    /// partition key at all, i.e. one bucket for the whole service — and reads as intentional
    /// because it sits beside a correctly partitioned global limiter.
    /// </summary>
    [Test]
    public async Task TheSearchPolicyIsPartitionedPerClientToo()
    {
        const string noisy = "192.0.2.10";
        const string quiet = "192.0.2.20";

        var noisyStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < RateLimitingApiFactory.SearchPermitLimit + 2; i++)
        {
            noisyStatuses.Add(await GetAsAsync(noisy, SearchLimitedEndpoint));
        }

        noisyStatuses.Should().Contain(HttpStatusCode.TooManyRequests);

        (await GetAsAsync(quiet, SearchLimitedEndpoint)).Should().NotBe(HttpStatusCode.TooManyRequests,
            "the search policy must not be one bucket shared by every caller");
    }
}
