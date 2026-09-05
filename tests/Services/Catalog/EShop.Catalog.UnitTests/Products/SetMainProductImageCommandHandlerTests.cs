using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Commands.SetMainProductImage;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class SetMainProductImageCommandHandlerTests
{
    private Mock<IProductRepository> _productRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<ICacheInvalidator> _cacheInvalidatorMock = null!;
    private SetMainProductImageCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _cacheInvalidatorMock = new Mock<ICacheInvalidator>();
        _handler = new SetMainProductImageCommandHandler(
            _productRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cacheInvalidatorMock.Object);
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldSwitchMainImage()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, categoryId);
        var firstImageId = product.AddImage("https://example.com/first.jpg", "First", 0);
        var secondImageId = product.AddImage("https://example.com/second.jpg", "Second", 1);

        var command = new SetMainProductImageCommand { ProductId = product.Id, ImageId = secondImageId };

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
        Assert.That(product.Images.Count(i => i.IsMain), Is.EqualTo(1), "exactly one main image must remain");
        Assert.That(product.Images.Single(i => i.Id == secondImageId).IsMain, Is.True);
        Assert.That(product.Images.Single(i => i.Id == firstImageId).IsMain, Is.False);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cacheInvalidatorMock.Verify(
            x => x.InvalidateAsync($"products:category:{categoryId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentProduct_ShouldReturnNotFoundError()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var command = new SetMainProductImageCommand { ProductId = productId, ImageId = Guid.NewGuid() };

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
    public async Task Handle_WithNonExistentImage_ShouldReturnImageNotFoundError()
    {
        // Arrange — must be a Result failure, not a DomainException: the endpoint owes a 404,
        // and DomainException would surface as 400.
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, Guid.NewGuid());
        var existingImageId = product.AddImage("https://example.com/img.jpg", "Alt text", 0);

        var command = new SetMainProductImageCommand { ProductId = product.Id, ImageId = Guid.NewGuid() };

        _productRepositoryMock
            .Setup(x => x.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("ProductImage.NotFound"));
        Assert.That(product.Images.Single(i => i.Id == existingImageId).IsMain, Is.True, "the existing main image is untouched");
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
