using EShop.Ordering.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace EShop.Ordering.IntegrationTests.Configuration;

/// <summary>
/// Orders are priced from Catalog, so a deployment that forgets <c>CatalogService:BaseUrl</c> must
/// not boot looking healthy and then fail every order. <c>ValidateOnStart</c> is what promises that;
/// this is what keeps the promise from being quietly dropped.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CatalogConfigurationTests
{
    [TestCase("")]
    [TestCase("not-a-url")]
    public void TheHostRefusesToStart_WithoutAnAbsoluteCatalogUrl(string baseUrl)
    {
        using var factory = new CatalogUrlFactory(baseUrl);

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>()
            .Where(e => Flatten(e).Any(m => m.Contains("CatalogService:BaseUrl")),
                "the startup failure must name the missing setting");
    }

    private static IEnumerable<string> Flatten(Exception e)
    {
        for (Exception? current = e; current is not null; current = current.InnerException)
        {
            yield return current.Message;

            if (current is AggregateException aggregate)
            {
                foreach (var message in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return message;
                }
            }
        }
    }

    private sealed class CatalogUrlFactory(string baseUrl) : OrderingApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("CatalogService:BaseUrl", baseUrl);
        }
    }
}
