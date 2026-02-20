using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Catalog.Application.Products.EventHandlers;
using EShop.Catalog.Domain.Events;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class ProductCreatedEventHandlerTests
{
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private Mock<ICurrentUserContext> _currentUserContextMock = null!;
    private Mock<ILogger<ProductCreatedEventHandler>> _loggerMock = null!;
    private ProductCreatedEventHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _currentUserContextMock = new Mock<ICurrentUserContext>();
        _loggerMock = new Mock<ILogger<ProductCreatedEventHandler>>();

        _currentUserContextMock.Setup(x => x.CorrelationId).Returns("test-correlation-id");

        _handler = new ProductCreatedEventHandler(
            _outboxMock.Object,
            _currentUserContextMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task Handle_ShouldEnqueueIntegrationEvent()
    {
        // Arrange
        var domainEvent = new ProductCreatedEvent
        {
            ProductId = Guid.NewGuid(),
            ProductName = "Test Product",
            Price = 29.99m
        };

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _outboxMock.Verify(
            x => x.Enqueue(
                It.Is<ProductCreatedIntegrationEvent>(e =>
                    e.ProductId == domainEvent.ProductId &&
                    e.ProductName == domainEvent.ProductName &&
                    e.Price == domainEvent.Price &&
                    e.CorrelationId == "test-correlation-id"),
                "test-correlation-id"),
            Times.Once);
    }
}
