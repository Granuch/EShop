using EShop.Basket.Application.Common;
using EShop.Basket.Application.Queries.GetBasket;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class GetBasketQueryHandlerTests
{
    private static GetBasketQueryHandler Handler(Mock<IBasketRepository> repository)
        => new(repository.Object, Mock.Of<ILogger<GetBasketQueryHandler>>());

    /// <summary>Basket audit S8 (D8): no basket reads as an empty one, not as "not found".</summary>
    [Test]
    public async Task Handle_WhenBasketNotFound_ShouldReturnAnEmptyBasket()
    {
        var repository = new Mock<IBasketRepository>();
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingBasket?)null);

        var result = await Handler(repository).Handle(new GetBasketQuery { UserId = "user-1" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.UserId, Is.EqualTo("user-1"));
        Assert.That(result.Value.Items, Is.Empty);
        Assert.That(result.Value.TotalPrice, Is.EqualTo(0m));
        Assert.That(result.Value.CreatedAt, Is.Null);
    }

    [Test]
    public async Task Handle_WhenBasketExists_ShouldMapBasketToDto()
    {
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Phone", 100m, 2);

        var repository = new Mock<IBasketRepository>();
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(basket);

        var result = await Handler(repository).Handle(new GetBasketQuery { UserId = "user-1" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.UserId, Is.EqualTo("user-1"));
        Assert.That(result.Value.Items, Has.Count.EqualTo(1));
        Assert.That(result.Value.TotalPrice, Is.EqualTo(200m));
        Assert.That(result.Value.TotalItems, Is.EqualTo(2));
        Assert.That(result.Value.CreatedAt, Is.EqualTo(basket.CreatedAt));
    }

    /// <summary>Basket audit S8 (M3): an unreadable store is an operation failure (503), not an unhandled 500.</summary>
    [Test]
    public async Task Handle_WhenTheStoreFails_ShouldReturnOperationFailed()
    {
        var repository = new Mock<IBasketRepository>();
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var result = await Handler(repository).Handle(new GetBasketQuery { UserId = "user-1" }, CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
    }
}
