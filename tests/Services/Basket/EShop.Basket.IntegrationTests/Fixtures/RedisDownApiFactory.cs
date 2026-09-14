using EShop.Basket.Domain.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EShop.Basket.IntegrationTests.Fixtures;

/// <summary>
/// Basket audit S8 (M3). A host whose basket store fails on every call, the way it does while Redis is down, so a test
/// can see what status an outage reaches the client as. Checkout's own store is left real, so checkout gets as far as
/// reading the basket.
/// </summary>
public sealed class RedisDownApiFactory : BasketApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            var repository = new Mock<IBasketRepository>();
            var outage = new InvalidOperationException("Redis is down (test double).");
            repository.Setup(x => x.GetBasketAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(outage);
            repository.Setup(x => x.DeleteBasketAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(outage);

            services.AddScoped(_ => repository.Object);
        });
    }
}
