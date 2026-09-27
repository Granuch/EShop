using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.RateLimiting;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.UnitTests.Http;

/// <summary>
/// Frontend-contracts F-05. <c>RejectionStatusCode</c> alone answers an empty 429, which is what the gateway and five
/// services did; the shared writer gives every component Identity's problem+json 429 with <c>Retry-After</c>.
/// </summary>
[TestFixture]
public class EShopRateLimitingTests
{
    private static DefaultHttpContext CreateContext()
        => new()
        {
            // Results.Problem resolves services at execute time; Response.Body defaults to Stream.Null.
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
            TraceIdentifier = "trace-429"
        };

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    [Test]
    public void UseEShopRejectionResponse_Sets429AndTheSharedWriter()
    {
        var options = new RateLimiterOptions().UseEShopRejectionResponse();

        Assert.That(options.RejectionStatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        Assert.That(options.OnRejected, Is.Not.Null);
        Assert.That(options.OnRejected!.Method.Name, Is.EqualTo(nameof(EShopRateLimiting.WriteRejectionAsync)));
    }

    [Test]
    public async Task WriteRejection_WritesTheProblemEnvelope()
    {
        var context = CreateContext();
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await EShopRateLimiting.WriteRejectionAsync(
            new OnRejectedContext { HttpContext = context, Lease = new FakeLease(null) }, CancellationToken.None);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        Assert.That(context.Response.ContentType, Does.StartWith("application/problem+json"));

        var body = await ReadBodyAsync(context);
        Assert.That(body.GetProperty("status").GetInt32(), Is.EqualTo(429));
        Assert.That(body.GetProperty("errorCode").GetString(), Is.EqualTo("Request.RateLimited"));
        Assert.That(body.GetProperty("traceId").GetString(), Is.EqualTo("trace-429"));
        Assert.That(body.GetProperty("detail").GetString(), Is.EqualTo(EShopRateLimiting.RejectionDetail));
    }

    [Test]
    public async Task WriteRejection_AdvertisesTheWindowAsRetryAfter()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromSeconds(60),
            QueueLimit = 0,
            AutoReplenishment = false
        });
        using var granted = limiter.AttemptAcquire();
        using var refused = limiter.AttemptAcquire();
        Assert.That(refused.IsAcquired, Is.False);

        var context = CreateContext();
        await EShopRateLimiting.WriteRejectionAsync(
            new OnRejectedContext { HttpContext = context, Lease = refused }, CancellationToken.None);

        Assert.That(context.Response.Headers.RetryAfter.ToString(), Is.EqualTo("60"));
    }

    [TestCase(0.4, "1")]
    [TestCase(59.2, "60")]
    [TestCase(0.0, "1")]
    public async Task WriteRejection_RoundsRetryAfterUp_AndNeverAdvertisesZero(double seconds, string expected)
    {
        var context = CreateContext();

        await EShopRateLimiting.WriteRejectionAsync(
            new OnRejectedContext { HttpContext = context, Lease = new FakeLease(TimeSpan.FromSeconds(seconds)) },
            CancellationToken.None);

        Assert.That(context.Response.Headers.RetryAfter.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public async Task WriteRejection_OmitsRetryAfter_WhenTheLimiterCannotTell()
    {
        var context = CreateContext();

        await EShopRateLimiting.WriteRejectionAsync(
            new OnRejectedContext { HttpContext = context, Lease = new FakeLease(null) }, CancellationToken.None);

        Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
    }

    private sealed class FakeLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames
            => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, [NotNullWhen(true)] out object? metadata)
        {
            metadata = retryAfter is not null && metadataName == MetadataName.RetryAfter.Name ? retryAfter.Value : null;
            return metadata is not null;
        }
    }
}
