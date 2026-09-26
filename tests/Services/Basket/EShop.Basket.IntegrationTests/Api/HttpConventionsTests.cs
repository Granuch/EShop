using System.Net;
using System.Text;
using System.Text.Json;
using EShop.Basket.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.Basket.IntegrationTests.Api;

/// <summary>
/// Frontend-contracts F-04, F-05 and F-20, pinned on the host as composed. No fixture can reach Basket's limiter (its
/// limit is hard-coded), so the structural check is the only guard on its 429.
/// </summary>
[TestFixture]
[Category("Integration")]
public class HttpConventionsTests
{
    private BasketApiFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _factory = new BasketApiFactory();

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

    /// <summary>F-20: a body that is not JSON was a bare 400 with an empty body and no errorCode.</summary>
    [TestCase("{\"productId\": ")]
    [TestCase("not json")]
    public async Task AMalformedBody_IsAProblemJson400(string body)
    {
        using var client = _factory.CreateClientFor("fe-r1-owner");

        using var response = await client.PostAsync(
            "/api/v1/basket/fe-r1-owner/items", new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertMalformedRequestAsync(response);
    }

    /// <summary>F-20: an unbindable query value fails binding before the endpoint runs, and was a bare 400 too.</summary>
    [Test]
    public async Task AnUnbindableQueryValue_IsAProblemJson400()
    {
        using var client = _factory.CreateClientWithPermissions("fe-r1-admin", "baskets.read");

        using var response = await client.GetAsync("/api/v1/basket/admin/carts?pageSize=abc");

        await AssertMalformedRequestAsync(response);
    }

    private static async Task AssertMalformedRequestAsync(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("errorCode").GetString(), Is.EqualTo("MalformedRequest"));
        Assert.That(json.RootElement.GetProperty("traceId").GetString(), Is.Not.Empty);
    }
}
