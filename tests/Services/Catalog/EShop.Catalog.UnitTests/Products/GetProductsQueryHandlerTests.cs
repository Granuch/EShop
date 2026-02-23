using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class GetProductsQueryHandlerTests
{
    private Mock<IProductQueryService> _productQueryServiceMock = null!;
    private GetProductsQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productQueryServiceMock = new Mock<IProductQueryService>();
        _handler = new GetProductsQueryHandler(_productQueryServiceMock.Object);
    }

    [Test]
    public async Task Handle_WithDefaultQuery_ShouldReturnPagedResult()
    {
        // Arrange
        var query = new GetProductsQuery { PageNumber = 1, PageSize = 10 };
        var dtos = new List<ProductDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Product 1", Sku = "SKU-001", Price = 10m },
            new() { Id = Guid.NewGuid(), Name = "Product 2", Sku = "SKU-002", Price = 20m }
        };

        _productQueryServiceMock
            .Setup(x => x.GetFilteredProductsAsync(
                null, null, null, null,
                ProductSortBy.Name, false, 1, 10,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((dtos, 2));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Items.Count(), Is.EqualTo(2));
        Assert.That(result.Value.TotalCount, Is.EqualTo(2));
        Assert.That(result.Value.PageNumber, Is.EqualTo(1));
        Assert.That(result.Value.PageSize, Is.EqualTo(10));
    }

    [Test]
    public async Task Handle_WithCategoryFilter_ShouldPassCategoryId()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var query = new GetProductsQuery
        {
            PageNumber = 1,
            PageSize = 10,
            CategoryId = categoryId
        };

        _productQueryServiceMock
            .Setup(x => x.GetFilteredProductsAsync(
                categoryId, null, null, null,
                ProductSortBy.Name, false, 1, 10,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ProductDto>(), 0));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _productQueryServiceMock.Verify(
            x => x.GetFilteredProductsAsync(
                categoryId, null, null, null,
                ProductSortBy.Name, false, 1, 10,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_WithSearchTerm_ShouldPassSearchTerm()
    {
        // Arrange
        var query = new GetProductsQuery
        {
            PageNumber = 1,
            PageSize = 10,
            SearchTerm = "laptop"
        };

        _productQueryServiceMock
            .Setup(x => x.GetFilteredProductsAsync(
                null, "laptop", null, null,
                ProductSortBy.Name, false, 1, 10,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ProductDto>(), 0));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _productQueryServiceMock.Verify(
            x => x.GetFilteredProductsAsync(
                null, "laptop", null, null,
                ProductSortBy.Name, false, 1, 10,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_EmptyResult_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var query = new GetProductsQuery { PageNumber = 1, PageSize = 10 };

        _productQueryServiceMock
            .Setup(x => x.GetFilteredProductsAsync(
                null, null, null, null,
                ProductSortBy.Name, false, 1, 10,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ProductDto>(), 0));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Items.Count(), Is.EqualTo(0));
        Assert.That(result.Value.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Handle_WithPriceFilter_ShouldPassPriceRange()
    {
        // Arrange
        var query = new GetProductsQuery
        {
            PageNumber = 1,
            PageSize = 10,
            MinPrice = 10m,
            MaxPrice = 100m
        };

        _productQueryServiceMock
            .Setup(x => x.GetFilteredProductsAsync(
                null, null, 10m, 100m,
                ProductSortBy.Name, false, 1, 10,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ProductDto>(), 0));

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        _productQueryServiceMock.Verify(
            x => x.GetFilteredProductsAsync(
                null, null, 10m, 100m,
                ProductSortBy.Name, false, 1, 10,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
