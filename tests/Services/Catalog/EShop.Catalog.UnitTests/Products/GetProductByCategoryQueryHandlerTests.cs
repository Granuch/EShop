using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Application.Products.Queries.GetProductByCategory;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class GetProductByCategoryQueryHandlerTests
{
    private Mock<IProductQueryService> _productQueryServiceMock = null!;
    private GetProductByCategoryQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productQueryServiceMock = new Mock<IProductQueryService>();
        _handler = new GetProductByCategoryQueryHandler(_productQueryServiceMock.Object);
    }

    [Test]
    public async Task Handle_ReturnsTheRequestedPageWithTheTotal()
    {
        var categoryId = Guid.NewGuid();
        var items = new List<ProductDto> { new() { Id = Guid.NewGuid(), Name = "A" } };

        _productQueryServiceMock
            .Setup(x => x.GetFilteredProductsAsync(
                new ProductListFilter(categoryId, null, null, null, IncludeUnpublished: false),
                ProductSortBy.Name, false, 3, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, 251));

        var result = await _handler.Handle(
            new GetProductByCategoryQuery { CategoryId = categoryId, PageNumber = 3, PageSize = 25 },
            CancellationToken.None);

        // D5: the total is the point — the old response was a bare list that stopped at 200
        // with nothing to say rows were missing.
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Items, Is.EqualTo(items));
        Assert.That(result.Value.TotalCount, Is.EqualTo(251));
        Assert.That(result.Value.PageNumber, Is.EqualTo(3));
        Assert.That(result.Value.PageSize, Is.EqualTo(25));
    }

    [Test]
    public async Task Handle_WithNoPaging_DefaultsToTheFirstPageOfTen_PublishedOnly()
    {
        var categoryId = Guid.NewGuid();
        _productQueryServiceMock
            .Setup(x => x.GetFilteredProductsAsync(
                It.IsAny<ProductListFilter>(), It.IsAny<ProductSortBy>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ProductDto>(), 0));

        await _handler.Handle(new GetProductByCategoryQuery { CategoryId = categoryId }, CancellationToken.None);

        _productQueryServiceMock.Verify(x => x.GetFilteredProductsAsync(
            new ProductListFilter(categoryId, null, null, null, IncludeUnpublished: false),
            ProductSortBy.Name, false, 1, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The paged key cannot be evicted by exact key, so family membership is the only thing that
    /// keeps a cached category page from outliving a product write. Losing it would leave every
    /// functional test green and serve stale pages for the full TTL.
    /// </summary>
    [Test]
    public void CachesInTheProductListFamily_WithThePageInTheKey()
    {
        var categoryId = Guid.NewGuid();
        var page1 = new GetProductByCategoryQuery { CategoryId = categoryId, PageNumber = 1, PageSize = 10 };
        var page2 = page1 with { PageNumber = 2 };

        // Asserted through the interface: CachingBehavior only folds in a family version for an
        // IVersionedCacheKey, so a record that kept the CacheKeyFamily property but dropped the
        // interface would still pass a property check while caching unversioned.
        Assert.That(page1, Is.InstanceOf<IVersionedCacheKey>());
        Assert.That(((IVersionedCacheKey)page1).CacheKeyFamily, Is.EqualTo(ProductCacheFamilies.ProductList));
        Assert.That(page1.CacheKey, Is.Not.EqualTo(page2.CacheKey), "two pages must not share a cache entry");
    }
}
