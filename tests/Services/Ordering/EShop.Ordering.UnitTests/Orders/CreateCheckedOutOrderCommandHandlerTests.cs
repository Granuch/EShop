using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Orders.Commands.CreateCheckedOutOrder;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

/// <summary>
/// Checkout keeps Basket's prices — the ones the customer saw — rather than repricing from Catalog.
/// </summary>
[TestFixture]
public class CreateCheckedOutOrderCommandHandlerTests
{
    [Test]
    public async Task Handle_CreatesTheOrderAtTheCheckedOutPrices()
    {
        var repository = new Mock<IOrderRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        Order? added = null;
        repository
            .Setup(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((o, _) => added = o);

        var handler = new CreateCheckedOutOrderCommandHandler(repository.Object, unitOfWork.Object);
        var command = new CreateCheckedOutOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "Springfield",
            State = "IL",
            ZipCode = "62701",
            Country = "US",
            Items =
            [
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget A", UnitPrice = 20.00m, Quantity = 2 },
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget B", UnitPrice = 5.50m, Quantity = 1 }
            ]
        };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(added!.TotalPrice, Is.EqualTo(45.50m));
        Assert.That(added.UserId, Is.EqualTo("user-1"));
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
