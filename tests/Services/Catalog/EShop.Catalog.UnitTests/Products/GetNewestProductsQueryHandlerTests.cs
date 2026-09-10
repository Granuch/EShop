using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetNewestProducts;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class GetNewestProductsQueryHandlerTests
{
    private Mock<IProductQueryService> _productQueryServiceMock = null!;
    private GetNewestProductsQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productQueryServiceMock = new Mock<IProductQueryService>();
        _handler = new GetNewestProductsQueryHandler(_productQueryServiceMock.Object);
    }

    private static List<ProductDto> Rows(int count)
    {
        var start = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count)
            .Select(i => new ProductDto { Id = Guid.NewGuid(), Name = $"P{i}", CreatedAt = start.AddMinutes(-i) })
            .ToList();
    }

    private void ServiceReturns(List<ProductDto> rows)
        => _productQueryServiceMock
            .Setup(x => x.GetNewestProductsAsync(
                It.IsAny<ProductListFilter>(), It.IsAny<ProductCursor?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    [Test]
    public async Task Handle_WhenAnotherRowExists_ReturnsOnePageAndACursorAtItsLastItem()
    {
        var rows = Rows(4);
        ServiceReturns(rows);

        var result = await _handler.Handle(new GetNewestProductsQuery { PageSize = 3 }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var page = result.Value;
        Assert.That(page.Items.Select(p => p.Id), Is.EqualTo(rows.Take(3).Select(p => p.Id)),
            "the look-ahead row must not leak into the page");
        Assert.That(page.HasNextPage, Is.True);

        Assert.That(ProductCursor.TryDecode(page.NextCursor, out var next), Is.True);
        Assert.That(next, Is.EqualTo(new ProductCursor(rows[2].CreatedAt, rows[2].Id)),
            "the next page starts strictly after the last item shown, not after the look-ahead row");
    }

    [Test]
    public async Task Handle_WhenTheRemainderExactlyFillsThePage_IssuesNoCursor()
    {
        // The case "a full page means there is more" gets wrong: it would hand out a cursor to an
        // empty page. The old offset-shaped response reported hasNextPage=true on the last page.
        ServiceReturns(Rows(3));

        var result = await _handler.Handle(new GetNewestProductsQuery { PageSize = 3 }, CancellationToken.None);

        Assert.That(result.Value.Items.Count(), Is.EqualTo(3));
        Assert.That(result.Value.NextCursor, Is.Null);
        Assert.That(result.Value.HasNextPage, Is.False);
    }

    [Test]
    public async Task Handle_AsksForExactlyOneRowMoreThanThePage()
    {
        ServiceReturns([]);

        await _handler.Handle(new GetNewestProductsQuery { PageSize = 7 }, CancellationToken.None);

        _productQueryServiceMock.Verify(x => x.GetNewestProductsAsync(
            It.IsAny<ProductListFilter>(), null, 8, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_PassesTheDecodedCursorAndTheFilterThrough()
    {
        ServiceReturns([]);
        var categoryId = Guid.NewGuid();
        var position = new ProductCursor(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Guid.NewGuid());

        await _handler.Handle(new GetNewestProductsQuery
        {
            Cursor = position.Encode(),
            CategoryId = categoryId,
            SearchTerm = "lamp",
            MinPrice = 1m,
            MaxPrice = 2m,
            IncludeUnpublished = true
        }, CancellationToken.None);

        _productQueryServiceMock.Verify(x => x.GetNewestProductsAsync(
            new ProductListFilter(categoryId, "lamp", 1m, 2m, IncludeUnpublished: true),
            position,
            11,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNoCursor_StartsFromTheNewestAndDefaultsToPublishedOnly()
    {
        ServiceReturns([]);

        await _handler.Handle(new GetNewestProductsQuery(), CancellationToken.None);

        _productQueryServiceMock.Verify(x => x.GetNewestProductsAsync(
            new ProductListFilter(null, null, null, null, IncludeUnpublished: false),
            null,
            11,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
