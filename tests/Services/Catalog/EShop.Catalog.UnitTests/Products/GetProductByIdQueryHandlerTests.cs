using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Application.Products.Queries.GetProductsById;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using MapsterMapper;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class GetProductByIdQueryHandlerTests
{
    private Mock<IProductRepository> _productRepositoryMock = null!;
    private Mock<IMapper> _mapperMock = null!;
    private GetProductByIdQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _mapperMock = new Mock<IMapper>();
        _handler = new GetProductByIdQueryHandler(
            _productRepositoryMock.Object,
            _mapperMock.Object);
    }

    [Test]
    public async Task Handle_WithExistingProduct_ShouldReturnProductDto()
    {
        // Arrange
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, Guid.NewGuid());
        var query = new GetProductByIdQuery { ProductId = product.Id };

        var expectedDto = new ProductDto
        {
            Id = product.Id,
            Name = "Test Product",
            Sku = "SKU-001",
            Price = 29.99m
        };

        _productRepositoryMock
            .Setup(x => x.GetByIdReadOnlyAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mapperMock
            .Setup(x => x.Map<ProductDto>(product))
            .Returns(expectedDto);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Id, Is.EqualTo(product.Id));
        Assert.That(result.Value.Name, Is.EqualTo("Test Product"));
        Assert.That(result.Value.Sku, Is.EqualTo("SKU-001"));
        Assert.That(result.Value.Price, Is.EqualTo(29.99m));
    }

    [Test]
    public async Task Handle_WithNonExistentProduct_ShouldReturnNotFoundError()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var query = new GetProductByIdQuery { ProductId = productId };

        _productRepositoryMock
            .Setup(x => x.GetByIdReadOnlyAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Product.NotFound"));
    }

    [Test]
    public async Task Handle_ShouldUseReadOnlyQuery()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var query = new GetProductByIdQuery { ProductId = productId };

        _productRepositoryMock
            .Setup(x => x.GetByIdReadOnlyAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert — Verify read-only repository method (AsNoTracking) is used, not GetByIdAsync
        _productRepositoryMock.Verify(
            x => x.GetByIdReadOnlyAsync(productId, It.IsAny<CancellationToken>()), Times.Once);
        _productRepositoryMock.Verify(
            x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
