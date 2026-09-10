using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class GetProductsQueryHandlerTests
{
    private Mock<IProductQueryService> _productQueryServiceMock = null!;
    private GetProductsQueryHandler _handler = null!;

    // Public caller, no filters — what every test here starts from.
    private static readonly ProductListFilter NoFilter = new(null, null, null, null, IncludeUnpublished: false);

    [SetUp]
    public void SetUp()
    {
        _productQueryServiceMock = new Mock<IProductQueryService>();
        _handler = new GetProductsQueryHandler(_productQueryServiceMock.Object);
    }

    private void Returns(ProductListFilter filter, List<ProductDto> items, int total)
        => _productQueryServiceMock
            .Setup(x => x.GetFilteredProductsAsync(
                filter, ProductSortBy.Name, false, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, total));

    private void VerifyCalledWith(ProductListFilter filter)
        => _productQueryServiceMock.Verify(
            x => x.GetFilteredProductsAsync(
                filter, ProductSortBy.Name, false, 1, 10, It.IsAny<CancellationToken>()),
            Times.Once);

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
        Returns(NoFilter, dtos, 2);

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
        var query = new GetProductsQuery { PageNumber = 1, PageSize = 10, CategoryId = categoryId };
        var filter = NoFilter with { CategoryId = categoryId };
        Returns(filter, [], 0);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        VerifyCalledWith(filter);
    }

    [Test]
    public async Task Handle_WithSearchTerm_ShouldPassSearchTerm()
    {
        // Arrange
        var query = new GetProductsQuery { PageNumber = 1, PageSize = 10, SearchTerm = "laptop" };
        var filter = NoFilter with { SearchTerm = "laptop" };
        Returns(filter, [], 0);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        VerifyCalledWith(filter);
    }

    [Test]
    public async Task Handle_EmptyResult_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var query = new GetProductsQuery { PageNumber = 1, PageSize = 10 };
        Returns(NoFilter, [], 0);

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
        var query = new GetProductsQuery { PageNumber = 1, PageSize = 10, MinPrice = 10m, MaxPrice = 100m };
        var filter = NoFilter with { MinPrice = 10m, MaxPrice = 100m };
        Returns(filter, [], 0);

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        VerifyCalledWith(filter);
    }

    [Test]
    public async Task Handle_ForAnAdmin_ShouldIncludeUnpublished()
    {
        // The endpoint sets this from the caller's role; the handler must carry it into the filter
        // rather than defaulting it, or admins could never see a draft in a list.
        var query = new GetProductsQuery { PageNumber = 1, PageSize = 10, IncludeUnpublished = true };
        var filter = NoFilter with { IncludeUnpublished = true };
        Returns(filter, [], 0);

        await _handler.Handle(query, CancellationToken.None);

        VerifyCalledWith(filter);
    }
}
