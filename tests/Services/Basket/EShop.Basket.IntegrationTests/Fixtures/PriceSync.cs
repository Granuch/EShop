using EShop.Basket.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EShop.Basket.IntegrationTests.Fixtures;

/// <summary>
/// Runs Basket's real <see cref="ProductPriceChangedConsumer"/>, resolved from the host's container, for one price change
/// — the whole consumer (idempotency store, repository, retries) against the fixture's real Redis. Only the MassTransit
/// context is a stub; the Testing host has no broker.
/// </summary>
public static class PriceSync
{
    public static async Task RunAsync(BasketApiFactory factory, Guid productId, decimal newPrice)
    {
        using var scope = factory.Services.CreateScope();
        var consumer = ActivatorUtilities.CreateInstance<ProductPriceChangedConsumer>(scope.ServiceProvider);

        var context = new Mock<ConsumeContext<ProductPriceChangedIntegrationEvent>>();
        context.SetupGet(x => x.Message).Returns(new ProductPriceChangedIntegrationEvent
        {
            ProductId = productId,
            OldPrice = 0m,
            NewPrice = newPrice
        });
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);
    }
}
