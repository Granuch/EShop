using System.Text.Json;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Notification.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.Notification.IntegrationTests.Api;

/// <summary>
/// Frontend-contracts F-04 and F-05, pinned on the host as composed: the frontend CORS policy exposes the headers a
/// browser client must read, and the rate limiter answers with the shared problem+json 429. No fixture here can reach
/// Notification's limiter, so this structural check is the only guard on its 429.
/// </summary>
[TestFixture]
[Category("Integration")]
public class HttpConventionsTests
{
    [Test]
    public async Task TheRateLimiter_AnswersWithTheSharedProblemResponse()
    {
        await using var factory = new NotificationApiFactory();
        var options = factory.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        Assert.That(options.RejectionStatusCode, Is.EqualTo(429));
        Assert.That(options.OnRejected?.Method,
            Is.EqualTo(typeof(EShopRateLimiting).GetMethod(nameof(EShopRateLimiting.WriteRejectionAsync))));
    }

    [Test]
    public async Task TheFrontendCorsPolicy_ExposesTheHeadersABrowserClientReads()
    {
        await using var factory = new NotificationApiFactory();
        var policy = factory.Services.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("AllowFrontend");

        Assert.That(policy, Is.Not.Null);
        Assert.That(policy!.ExposedHeaders, Is.EquivalentTo(EShopCors.ExposedHeaders));
    }

    /// <summary>
    /// Frontend-contracts F-01: an enum is its PascalCase name on the wire, read in any case, and a number is refused.
    /// </summary>
    [Test]
    public async Task Enums_AreWrittenAsNames_AndReadOnlyAsNames()
    {
        await using var factory = new NotificationApiFactory();
        var json = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        AssertEnumsAreNames(json);
    }

    private static void AssertEnumsAreNames(JsonSerializerOptions json) => Assert.Multiple(() =>
    {
        Assert.That(JsonSerializer.Serialize(DayOfWeek.Tuesday, json), Is.EqualTo("\"Tuesday\""));
        Assert.That(JsonSerializer.Deserialize<DayOfWeek>("\"tuesday\"", json), Is.EqualTo(DayOfWeek.Tuesday));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DayOfWeek>("2", json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DayOfWeek>("\"2\"", json));
    });
}
