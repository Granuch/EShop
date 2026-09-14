using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace EShop.Basket.IntegrationTests.Configuration;

/// <summary>
/// Basket audit C1 (D1). Every item added to a basket is priced from Catalog, and no deployment used to tell Basket
/// where Catalog was: the tracked <c>appsettings.json</c> supplied <c>http://localhost:8082/</c>, which inside a
/// container is Basket itself. The host booted healthy and every add-to-basket failed with a 400.
///
/// <para>Two things now prevent that, and these tests pin both: <c>ValidateOnStart</c> refuses a missing or relative
/// URL, and the tracked settings carry no URL to fall back on. The Production cases boot <c>Program.cs</c> on the
/// tracked files, which is the only way to see the second; the control case supplies a URL and must start, or the
/// refusal would prove nothing.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CatalogConfigurationTests
{
    private const string RealKey = "k7Qp2vXw9sLm4tRz8bNc6yHd3fJg5aUe1oWi0";
    private const string RealOrigin = "https://shop.eshop-real.test";

    [TestCase("")]
    [TestCase("not-a-url")]
    public void TheHostRefusesToStart_WithoutAnAbsoluteCatalogUrl(string baseUrl)
    {
        using var factory = new CatalogUrlFactory(baseUrl);

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>()
            .Where(e => Messages(e).Any(m => m.Contains("CatalogService:BaseUrl")),
                "the startup failure must name the missing setting");
    }

    [Test]
    public void InProduction_TheTrackedSettingsSupplyNoCatalogUrl_SoTheHostRefusesToStart()
    {
        var failure = StartInProduction(catalogUrl: null);

        failure.Should().NotBeNull("with no deployment value there must be no tracked default to fall back on");
        Messages(failure).Should().Contain(m => m.Contains("CatalogService:BaseUrl"));
    }

    [Test]
    public void InProduction_WithACatalogUrl_TheHostStarts()
    {
        var failure = StartInProduction("http://catalog-api:8080/");

        failure.Should().BeNull("a configured URL must pass, or the refusal above proves nothing");
    }

    private static Exception? StartInProduction(string? catalogUrl)
    {
        using var factory = new ProductionHostFactory(catalogUrl);

        try
        {
            factory.CreateClient().Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static IEnumerable<string> Messages(Exception? e)
    {
        for (var current = e; current is not null; current = current.InnerException)
        {
            yield return current.Message;

            if (current is AggregateException aggregate)
            {
                foreach (var message in aggregate.InnerExceptions.SelectMany(Messages))
                {
                    yield return message;
                }
            }
        }
    }

    private sealed class CatalogUrlFactory(string baseUrl) : BasketApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("CatalogService:BaseUrl", baseUrl);
        }
    }

    /// <summary>
    /// <c>Program.cs</c> as Production, on the tracked <c>appsettings.json</c> and <c>appsettings.Production.json</c>.
    /// Only what the host needs before the Catalog check is supplied; Redis stays the base factory's mock and RabbitMQ
    /// is never contacted.
    /// </summary>
    private sealed class ProductionHostFactory(string? catalogUrl) : BasketApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment("Production");
            builder.UseSetting("JwtSettings:SecretKey", RealKey);
            builder.UseSetting("Cors:AllowedOrigins:0", RealOrigin);
            builder.UseSetting("ConnectionStrings:Redis", "redis.invalid:6379,abortConnect=false");
            builder.UseSetting("RabbitMQ:Host", "rabbitmq.invalid");
            builder.UseSetting("RabbitMQ:Username", "u");
            builder.UseSetting("RabbitMQ:Password", "p");
            builder.UseSetting("RabbitMQ:WaitUntilStarted", "false");

            if (catalogUrl is not null)
            {
                builder.UseSetting("CatalogService:BaseUrl", catalogUrl);
            }
        }
    }
}
