using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using static EShop.ApiGateway.UnitTests.Middleware.ProxyGuardAssertions;

namespace EShop.ApiGateway.UnitTests.Middleware;

[TestFixture]
public class CatalogProxyGuardMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_Returns413_WhenCatalogPayloadExceedsLimit()
    {
        var wasCalled = false;
        var middleware = CreateMiddleware(
            next: _ =>
            {
                wasCalled = true;
                return Task.CompletedTask;
            },
            maxBodySizeBytes: 10);

        var context = CreateContext("/api/v1/categories");
        context.Request.ContentLength = 11;

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(wasCalled, Is.False);
        await AssertProblemBodyAsync(context, "Request.PayloadTooLarge");
    }

    [Test]
    public async Task InvokeAsync_LetsAnImportThroughAboveTheGeneralCap_UpToItsOwn()
    {
        // G8 (admin panel S16). The largest legal import is several megabytes; the general cap would refuse it.
        var wasCalled = false;
        var middleware = CreateMiddleware(
            next: _ =>
            {
                wasCalled = true;
                return Task.CompletedTask;
            },
            maxBodySizeBytes: 10,
            importMaxBodySizeBytes: 100);

        var context = CreateContext("/api/v1/products/import");
        context.Request.ContentLength = 100;

        await middleware.InvokeAsync(context);

        Assert.That(wasCalled, Is.True);
        Assert.That(context.Response.StatusCode, Is.Not.EqualTo(StatusCodes.Status413PayloadTooLarge));
    }

    [Test]
    public async Task InvokeAsync_Returns413_WhenAnImportExceedsItsOwnCap()
    {
        var wasCalled = false;
        var middleware = CreateMiddleware(
            next: _ =>
            {
                wasCalled = true;
                return Task.CompletedTask;
            },
            maxBodySizeBytes: 10,
            importMaxBodySizeBytes: 100);

        var context = CreateContext("/api/v1/products/import");
        context.Request.ContentLength = 101;

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(wasCalled, Is.False);
        await AssertProblemBodyAsync(context, "Request.PayloadTooLarge");
    }

    [TestCase("/api/v1/products/bulk/price")]
    [TestCase("/api/v1/products")]
    [TestCase("/api/v1/products/importer")]
    public async Task InvokeAsync_KeepsTheGeneralCap_OffTheImportPath(string path)
    {
        // The larger cap is the import's alone. A bulk action fits in the general megabyte, and a path that merely
        // starts with the same letters ("importer") is a different segment.
        var middleware = CreateMiddleware(
            next: _ => Task.CompletedTask,
            maxBodySizeBytes: 10,
            importMaxBodySizeBytes: 100);

        var context = CreateContext(path);
        context.Request.ContentLength = 50;

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge), path);
    }

    [Test]
    public void TheImportCap_FitsTheLargestImportCatalogAccepts()
    {
        // 1 000 rows of 1 250 characters each (name 200, description 1000, SKU 50), escaped to six bytes a character by a
        // .NET client's default JSON encoder when not ASCII, plus a generous allowance for field names and numbers.
        const long largestLegalImport = 1_000L * ((200 + 1_000 + 50) * 6 + 200);

        Assert.That(new CatalogProxyOptions().ImportMaxRequestBodySizeBytes, Is.GreaterThanOrEqualTo(largestLegalImport));
    }

    [Test]
    public async Task InvokeAsync_Maps502To503_WithRetryAfter_ForCatalogPath()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            },
            retryAfterSeconds: 8);

        var context = CreateContext("/api/v1/products");

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(context.Response.Headers["Retry-After"].ToString(), Is.EqualTo("8"));
        await AssertProblemBodyAsync(context, "Gateway.UpstreamUnavailable");
    }

    [Test]
    public async Task InvokeAsync_DoesNotModifyNonCatalogPath()
    {
        var middleware = CreateMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
    }

    private static CatalogProxyGuardMiddleware CreateMiddleware(
        RequestDelegate next,
        long maxBodySizeBytes = 1024,
        int retryAfterSeconds = 5,
        long importMaxBodySizeBytes = 8_388_608)
    {
        var options = Options.Create(new CatalogProxyOptions
        {
            MaxRequestBodySizeBytes = maxBodySizeBytes,
            ImportMaxRequestBodySizeBytes = importMaxBodySizeBytes,
            UpstreamUnavailableRetryAfterSeconds = retryAfterSeconds
        });

        return new CatalogProxyGuardMiddleware(next, options);
    }
}
