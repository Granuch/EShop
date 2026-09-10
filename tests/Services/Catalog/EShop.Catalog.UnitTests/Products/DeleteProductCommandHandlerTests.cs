using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Application.Products.Commands.DeleteProduct;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class DeleteProductCommandHandlerTests
{
    private Mock<IProductRepository> _productRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<ILogger<DeleteProductCommandHandler>> _loggerMock = null!;
    private DeleteProductCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _loggerMock = new Mock<ILogger<DeleteProductCommandHandler>>();
        _handler = new DeleteProductCommandHandler(
            _productRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task Handle_WithExistingProduct_ShouldSoftDeleteAndReturnSuccess()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, categoryId);

        var command = new DeleteProductCommand { ProductId = product.Id };

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
        Assert.That(product.IsDeleted, Is.True);
        Assert.That(product.Status, Is.EqualTo(ProductStatus.Discontinued));
        _productRepositoryMock.Verify(x => x.UpdateAsync(product, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentProduct_ShouldReturnNotFoundError()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var command = new DeleteProductCommand { ProductId = productId };

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
    /// A deleted product must leave every list, the per-category pages included — which since
    /// Stage 6 is the family's job rather than an exact key the handler adds.
    /// </summary>
    [Test]
    public void DeclaresItsOwnInvalidation()
    {
        var command = new DeleteProductCommand { ProductId = Guid.NewGuid() };

        Assert.That(command.CacheKeysToInvalidate, Is.EquivalentTo(ProductCacheKeys.AllDetailVariants(command.ProductId)));
        Assert.That(command.CacheFamiliesToInvalidate, Does.Contain(ProductCacheFamilies.ProductList));
    }
}
