using System.Net;
using System.Text.Json;
using EShop.Ordering.IntegrationTests.Fixtures;

namespace EShop.Ordering.IntegrationTests.RateLimiting;

/// <summary>
/// Frontend-contracts F-05. A rejected request used to be an empty 429 with no <c>Retry-After</c>; it is now the shared
/// problem+json envelope, as Identity's always was.
/// </summary>
[TestFixture]
[Category("Integration")]
public class RateLimitRejectionTests : IntegrationTestBase
{
    protected override bool UseFixtureScopedHost => false;

    protected override async Task<OrderingApiFactory> CreateFactoryAsync()
        => await RateLimitingApiFactory.CreateAsync();

    [Test]
    public async Task ARejectedRequest_AnswersProblemJson_WithRetryAfter()
    {
        HttpResponseMessage? response = null;
        for (var i = 0; i <= RateLimitingApiFactory.GlobalPermitLimit; i++)
        {
            response?.Dispose();
            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Add(RateLimitingApiFactory.RemoteIpHeader, RateLimitingApiFactory.TrustedProxyIp);
            request.Headers.Add("X-Forwarded-For", "203.0.113.77");
            response = await Client.SendAsync(request);
        }

        using var rejected = response!;
        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(rejected.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(rejected.Headers.RetryAfter?.Delta?.TotalSeconds, Is.GreaterThanOrEqualTo(1));

        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.That(body.RootElement.GetProperty("errorCode").GetString(), Is.EqualTo("Request.RateLimited"));
        Assert.That(body.RootElement.GetProperty("status").GetInt32(), Is.EqualTo(429));
        Assert.That(body.RootElement.GetProperty("traceId").GetString(), Is.Not.Empty);
    }
}
