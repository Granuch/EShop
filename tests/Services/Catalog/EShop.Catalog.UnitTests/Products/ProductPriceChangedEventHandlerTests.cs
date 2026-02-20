using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Catalog.Application.Products.EventHandlers;
using EShop.Catalog.Domain.Events;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class ProductPriceChangedEventHandlerTests
{
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private Mock<ICurrentUserContext> _currentUserContextMock = null!;
    private Mock<ILogger<ProductPriceChangedEventHandler>> _loggerMock = null!;
    private ProductPriceChangedEventHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _currentUserContextMock = new Mock<ICurrentUserContext>();
        _loggerMock = new Mock<ILogger<ProductPriceChangedEventHandler>>();

        _currentUserContextMock.Setup(x => x.CorrelationId).Returns("corr-123");

        _handler = new ProductPriceChangedEventHandler(
            _outboxMock.Object,
            _currentUserContextMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task Handle_ShouldEnqueuePriceChangedIntegrationEvent()
    {
        // Arrange
        var domainEvent = new ProductPriceChangedEvent
        {
            ProductId = Guid.NewGuid(),
            OldPrice = 29.99m,
            NewPrice = 39.99m
        };

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _outboxMock.Verify(
            x => x.Enqueue(
                It.Is<ProductPriceChangedIntegrationEvent>(e =>
                    e.ProductId == domainEvent.ProductId &&
                    e.OldPrice == 29.99m &&
                    e.NewPrice == 39.99m &&
                    e.CorrelationId == "corr-123"),
                "corr-123"),
            Times.Once);
    }
}
