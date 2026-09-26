using EShop.ApiGateway.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.IntegrationTests.Api;

/// <summary>
/// Frontend-contracts F-04 and F-05. The gateway is the one host a browser client talks to, so its CORS policy is the
/// one that decides which response headers the client can read. No fixture can reach the gateway's limiter, so the
/// structural check is the only guard on its 429.
/// </summary>
[TestFixture]
[Category("Integration")]
public class HttpConventionsTests
{
    private GatewayApiFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _factory = new GatewayApiFactory();

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [Test]
    public void TheRateLimiter_AnswersWithTheSharedProblemResponse()
    {
        var options = _factory.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        Assert.That(options.RejectionStatusCode, Is.EqualTo(429));
        Assert.That(options.OnRejected?.Method,
            Is.EqualTo(typeof(EShopRateLimiting).GetMethod(nameof(EShopRateLimiting.WriteRejectionAsync))));
    }

    [Test]
    public void TheFrontendCorsPolicy_ExposesTheHeadersABrowserClientReads()
    {
        var policy = _factory.Services.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("AllowFrontend");

        Assert.That(policy, Is.Not.Null);
        Assert.That(policy!.ExposedHeaders, Is.EquivalentTo(EShopCors.ExposedHeaders));
    }

    [Test]
    public async Task ACrossOriginResponse_ListsTheExposedHeaders()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", "http://localhost:3000");

        using var response = await client.SendAsync(request);

        Assert.That(response.Headers.TryGetValues("Access-Control-Expose-Headers", out var values), Is.True,
            "without it a browser hides Location, Retry-After, X-Correlation-ID and Content-Disposition");
        var exposed = values!.SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries)).ToList();
        Assert.That(exposed, Is.SupersetOf(EShopCors.ExposedHeaders));
    }
}
