using EShop.Basket.Application.Queries.GetBasket;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class GetBasketQueryHandlerTests
{
    [Test]
    public async Task Handle_WhenBasketNotFound_ShouldReturnSuccessWithNullValue()
    {
        var repository = new Mock<IBasketRepository>();
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingBasket?)null);

        var handler = new GetBasketQueryHandler(repository.Object);

        var result = await handler.Handle(new GetBasketQuery { UserId = "user-1" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Null);
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

        var handler = new GetBasketQueryHandler(repository.Object);

        var result = await handler.Handle(new GetBasketQuery { UserId = "user-1" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.UserId, Is.EqualTo("user-1"));
        Assert.That(result.Value.Items, Has.Count.EqualTo(1));
        Assert.That(result.Value.TotalPrice, Is.EqualTo(200m));
        Assert.That(result.Value.TotalItems, Is.EqualTo(2));
    }
}
