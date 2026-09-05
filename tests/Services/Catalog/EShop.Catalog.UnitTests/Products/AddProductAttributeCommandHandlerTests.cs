using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Commands.AddProductAttribute;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class AddProductAttributeCommandHandlerTests
{
    private Mock<IProductRepository> _productRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<ICacheInvalidator> _cacheInvalidatorMock = null!;
    private AddProductAttributeCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _cacheInvalidatorMock = new Mock<ICacheInvalidator>();
        _handler = new AddProductAttributeCommandHandler(
            _productRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cacheInvalidatorMock.Object);
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldAddAttributeAndReturnItsId()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, categoryId);

        var command = new AddProductAttributeCommand
        {
            ProductId = product.Id,
            Name = "Color",
            Value = "Red"
        };

        _productRepositoryMock
            .Setup(x => x.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(product.Attributes, Has.Count.EqualTo(1));
        Assert.That(result.Value, Is.EqualTo(product.Attributes.Single().Id));
        Assert.That(product.Attributes.Single().Name, Is.EqualTo("Color"));
        Assert.That(product.Attributes.Single().Value, Is.EqualTo("Red"));
        _productRepositoryMock.Verify(x => x.UpdateAsync(product, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cacheInvalidatorMock.Verify(
            x => x.InvalidateAsync($"products:category:{categoryId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentProduct_ShouldReturnNotFoundError()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var command = new AddProductAttributeCommand
        {
            ProductId = productId,
            Name = "Color",
            Value = "Red"
        };

        _productRepositoryMock
            .Setup(x => x.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Product.NotFound"));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithDuplicateName_ShouldSucceed()
    {
        // Arrange — attributes are add-only and the domain permits duplicate names;
        // uniqueness is enforced only within a single create request (plan §7, gap #11).
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, Guid.NewGuid());
        product.AddAttribute("Color", "Red");

        var command = new AddProductAttributeCommand
        {
            ProductId = product.Id,
            Name = "Color",
            Value = "Blue"
        };

        _productRepositoryMock
            .Setup(x => x.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(product.Attributes, Has.Count.EqualTo(2));
    }
}
