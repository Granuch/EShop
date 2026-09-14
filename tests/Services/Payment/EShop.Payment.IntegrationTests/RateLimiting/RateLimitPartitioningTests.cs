using System.Net;
using EShop.Payment.IntegrationTests.Fixtures;

namespace EShop.Payment.IntegrationTests.RateLimiting;

/// <summary>
/// Payment audit Stage 3 (H4). Payment had no rate limiter, so a caller reaching payment-api directly could create
/// Stripe customers and intents without limit. It now has Ordering's global limiter, partitioned per client. These
/// tests exercise the whole chain: peer address → <c>UseForwardedHeaders</c> → <c>GetClientPartitionKey</c> →
/// bucket. They use the anonymous info endpoint, <c>/</c>; the global limiter applies to every endpoint alike,
/// except the Stripe webhook, which opts out.
/// </summary>
[TestFixture]
[Category("Integration")]
public class RateLimitPartitioningTests : IntegrationTestBase
{
    private const string Endpoint = "/";

    protected override PaymentApiFactory CreateFactory() => new RateLimitingPaymentApiFactory();

    private async Task<HttpStatusCode> SendAsAsync(string clientIp, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        // The simulated gateway is the TCP peer; the real client is named in X-Forwarded-For.
        request.Headers.Add(RateLimitingPaymentApiFactory.RemoteIpHeader, RateLimitingPaymentApiFactory.TrustedProxyIp);
        request.Headers.Add("X-Forwarded-For", clientIp);
        if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}");
        }

        using var response = await Client.SendAsync(request);
        return response.StatusCode;
    }

    private Task<HttpStatusCode> GetAsAsync(string clientIp) => SendAsAsync(clientIp, HttpMethod.Get, Endpoint);

    [Test]
    public async Task AClientPastItsAllowance_IsThrottled_AndAnotherIsNot()
    {
        var noisy = new List<HttpStatusCode>();
        for (var i = 0; i < RateLimitingPaymentApiFactory.GlobalPermitLimit + 2; i++)
        {
            noisy.Add(await GetAsAsync("203.0.113.10"));
        }

        Assert.That(noisy, Does.Contain(HttpStatusCode.TooManyRequests), "a client past its allowance is throttled");
        Assert.That(await GetAsAsync("203.0.113.20"), Is.Not.EqualTo(HttpStatusCode.TooManyRequests),
            "a second client has its own bucket");
    }

    [Test]
    public async Task DistinctForwardedClients_GetDistinctBuckets()
    {
        for (var i = 0; i < RateLimitingPaymentApiFactory.GlobalPermitLimit; i++)
        {
            Assert.That(await GetAsAsync("198.51.100.1"), Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
        }

        Assert.That(await GetAsAsync("198.51.100.2"), Is.Not.EqualTo(HttpStatusCode.TooManyRequests),
            "the partition key must come from X-Forwarded-For, not from the gateway's peer address");
    }

    [Test]
    public async Task Ipv4MappedIpv6AndPlainIpv4_ShareOneBucket()
    {
        for (var i = 0; i < RateLimitingPaymentApiFactory.GlobalPermitLimit; i++)
        {
            await GetAsAsync("203.0.113.30");
        }

        var mapped = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            mapped.Add(await GetAsAsync("::ffff:203.0.113.30"));
        }

        Assert.That(mapped, Does.Contain(HttpStatusCode.TooManyRequests),
            "the same host over a dual-stack socket must not double its allowance");
    }

    /// <summary>
    /// Stripe answers a 429 by redelivering later, so a throttled webhook only delays a payment being recorded. A
    /// client that has spent its allowance is still not throttled on <c>/webhooks/stripe</c>. Stripe is off in this
    /// host, so the webhook answers 404: anything but 429 shows the limiter let it through.
    /// </summary>
    [Test]
    public async Task TheStripeWebhook_IsNotRateLimited()
    {
        const string client = "203.0.113.40";
        var spent = new List<HttpStatusCode>();
        for (var i = 0; i < RateLimitingPaymentApiFactory.GlobalPermitLimit + 1; i++)
        {
            spent.Add(await GetAsAsync(client));
        }

        Assert.That(spent, Does.Contain(HttpStatusCode.TooManyRequests), "the client's allowance must be spent first");
        Assert.That(await SendAsAsync(client, HttpMethod.Post, "/webhooks/stripe"), Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
    }
}
