using System.Net;
using EShop.Ordering.IntegrationTests.Fixtures;
using FluentAssertions;

namespace EShop.Ordering.IntegrationTests.RateLimiting;

/// <summary>
/// Ordering audit L9. Ordering had no service-level rate limiter and relied on the gateway's alone, which
/// anything reaching ordering-api directly bypasses. It now has Catalog's global limiter, partitioned per
/// client. These tests exercise the whole chain: peer address → <c>UseForwardedHeaders</c> →
/// <c>GetClientPartitionKey</c> → bucket. They use the anonymous info endpoint, <c>/</c>, because every
/// order route requires a token and the global limiter applies to all of them alike.
/// </summary>
[TestFixture]
[Category("Integration")]
public class RateLimitPartitioningTests : IntegrationTestBase
{
    private const string Endpoint = "/";

    /// <summary>The buckets live in the host, so a shared host would let one test spend another's allowance.</summary>
    protected override bool UseFixtureScopedHost => false;

    protected override async Task<OrderingApiFactory> CreateFactoryAsync()
        => await RateLimitingApiFactory.CreateAsync();

    private async Task<HttpStatusCode> GetAsAsync(string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        // The simulated gateway is the TCP peer; the real client is named in X-Forwarded-For.
        request.Headers.Add(RateLimitingApiFactory.RemoteIpHeader, RateLimitingApiFactory.TrustedProxyIp);
        request.Headers.Add("X-Forwarded-For", clientIp);

        using var response = await Client.SendAsync(request);
        return response.StatusCode;
    }

    [Test]
    public async Task AClientPastItsAllowance_IsThrottled_AndAnotherIsNot()
    {
        var noisy = new List<HttpStatusCode>();
        for (var i = 0; i < RateLimitingApiFactory.GlobalPermitLimit + 2; i++)
        {
            noisy.Add(await GetAsAsync("203.0.113.10"));
        }

        noisy.Should().Contain(HttpStatusCode.TooManyRequests, "a client past its allowance is throttled");
        (await GetAsAsync("203.0.113.20")).Should().NotBe(HttpStatusCode.TooManyRequests,
            "a second client has its own bucket");
    }

    [Test]
    public async Task DistinctForwardedClients_GetDistinctBuckets()
    {
        for (var i = 0; i < RateLimitingApiFactory.GlobalPermitLimit; i++)
        {
            (await GetAsAsync("198.51.100.1")).Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        (await GetAsAsync("198.51.100.2")).Should().NotBe(HttpStatusCode.TooManyRequests,
            "the partition key must come from X-Forwarded-For, not from the gateway's peer address");
    }

    [Test]
    public async Task Ipv4MappedIpv6AndPlainIpv4_ShareOneBucket()
    {
        for (var i = 0; i < RateLimitingApiFactory.GlobalPermitLimit; i++)
        {
            await GetAsAsync("203.0.113.30");
        }

        var mapped = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            mapped.Add(await GetAsAsync("::ffff:203.0.113.30"));
        }

        mapped.Should().Contain(HttpStatusCode.TooManyRequests,
            "the same host over a dual-stack socket must not double its allowance");
    }
}
