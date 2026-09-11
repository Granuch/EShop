using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Application.Products.Commands.UpdateProduct;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class UpdateProductCommandHandlerTests
{
    private Mock<IProductRepository> _productRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private UpdateProductCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _handler = new UpdateProductCommandHandler(
            _productRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldUpdateAndReturnSuccess()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, categoryId);
        var productId = product.Id;

        var command = new UpdateProductCommand
        {
            ProductId = productId,
            Price = 39.99m,
            StockQuantity = 50
        };

        _productRepositoryMock
            .Setup(x => x.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _productRepositoryMock.Verify(x => x.UpdateAsync(product, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentProduct_ShouldReturnNotFoundError()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var command = new UpdateProductCommand
        {
            ProductId = productId,
            Price = 39.99m,
            StockQuantity = 50
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

    /// <summary>
    /// The handler has no cache collaborator any more, so these declarations are the whole of its
    /// invalidation. The category product pages used to be evicted by an exact key from the handler;
    /// since Stage 6 they are paged, live in the products:list family, and are covered by it.
    /// </summary>
    [Test]
    public void DeclaresItsOwnInvalidation()
    {
        var command = new UpdateProductCommand { ProductId = Guid.NewGuid() };

        Assert.That(command.CacheKeysToInvalidate, Is.EquivalentTo(ProductCacheKeys.AllDetailVariants(command.ProductId)));
        Assert.That(command.CacheFamiliesToInvalidate, Does.Contain(ProductCacheFamilies.ProductList));
    }
}
