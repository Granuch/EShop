using EShop.Catalog.Application.Products;
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
    public async Task Handle_WithExistingProduct_ShouldReturnProductDetailsDto()
    {
        // Arrange — published, because D1/H5a made the public detail path return Active products
        // only. Product.Create yields Draft, so a fixture that skips this asserts the pre-Stage-4
        // behaviour where the catalog served drafts.
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, Guid.NewGuid());
        product.Publish();
        var query = new GetProductByIdQuery { ProductId = product.Id };

        var expectedDto = new ProductDetailsDto
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
            .Setup(x => x.Map<ProductDetailsDto>(product))
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

    /// <summary>
    /// D1 / H5a. A draft is 404 to a public caller — deliberately the same error a missing product
    /// gives, so the response does not confirm that an unpublished product exists.
    /// </summary>
    [Test]
    public async Task Handle_WithUnpublishedProduct_ShouldReturnNotFoundForAPublicCaller()
    {
        var product = Product.Create("Draft Product", "SKU-002", 9.99m, 1, Guid.NewGuid());
        var query = new GetProductByIdQuery { ProductId = product.Id, IncludeUnpublished = false };

        _productRepositoryMock
            .Setup(x => x.GetByIdReadOnlyAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var result = await _handler.Handle(query, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Product.NotFound"));
    }

    /// <summary>
    /// The admin exemption. Without it a product would be invisible to whoever just created it,
    /// and there would be no way to review one before publishing.
    /// </summary>
    [Test]
    public async Task Handle_WithUnpublishedProduct_ShouldSucceedWhenUnpublishedAreIncluded()
    {
        var product = Product.Create("Draft Product", "SKU-003", 9.99m, 1, Guid.NewGuid());
        var query = new GetProductByIdQuery { ProductId = product.Id, IncludeUnpublished = true };

        _productRepositoryMock
            .Setup(x => x.GetByIdReadOnlyAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _mapperMock
            .Setup(x => x.Map<ProductDetailsDto>(product))
            .Returns(new ProductDetailsDto { Id = product.Id });

        var result = await _handler.Handle(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    /// <summary>
    /// The two visibility variants must not share a cache entry: the same product answers 200 to an
    /// admin and 404 to everyone else while it is unpublished, so one key would serve whichever
    /// answer was cached first to both.
    /// </summary>
    [Test]
    public void CacheKey_DiffersBetweenPublicAndUnpublishedVariants()
    {
        var id = Guid.NewGuid();

        var publicKey = new GetProductByIdQuery { ProductId = id, IncludeUnpublished = false }.CacheKey;
        var adminKey = new GetProductByIdQuery { ProductId = id, IncludeUnpublished = true }.CacheKey;

        Assert.That(publicKey, Is.Not.EqualTo(adminKey));
        Assert.That(
            ProductCacheKeys.AllDetailVariants(id),
            Is.EquivalentTo(new[] { publicKey, adminKey }),
            "every command evicts AllDetailVariants — a variant missing from it goes stale silently");
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
