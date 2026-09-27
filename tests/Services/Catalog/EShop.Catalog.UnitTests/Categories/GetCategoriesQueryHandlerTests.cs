using EShop.Catalog.Application.Categories.Queries.GetCategories;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Categories;

[TestFixture]
public class GetCategoriesQueryHandlerTests
{
    private Mock<ICategoryRepository> _categoryRepositoryMock = null!;
    private GetCategoriesQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _categoryRepositoryMock = new Mock<ICategoryRepository>();
        _handler = new GetCategoriesQueryHandler(_categoryRepositoryMock.Object);
    }

    private void RepositoryReturns(List<Category> categories)
        => _categoryRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);

    [Test]
    public async Task Handle_ShouldReturnAllRootCategories()
    {
        RepositoryReturns(
        [
            Category.Create("Electronics", "electronics", null),
            Category.Create("Clothing", "clothing", null)
        ]);

        var result = await _handler.Handle(new GetCategoriesQuery(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Handle_WithNoCategories_ShouldReturnEmptyList()
    {
        RepositoryReturns([]);

        var result = await _handler.Handle(new GetCategoriesQuery(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(0));
    }

    /// <summary>
    /// F-37. The Include chain this replaced stopped at grandchildren, so the fourth level vanished
    /// and the third looked like a leaf. Five levels, so the deepest is two below the old limit.
    /// </summary>
    [Test]
    public async Task Handle_BuildsTheTreeAtAnyDepth()
    {
        var chain = new List<Category> { Category.Create("L1", "l1", null) };
        for (var level = 2; level <= 5; level++)
            chain.Add(Category.Create($"L{level}", $"l{level}", chain[^1]));

        RepositoryReturns(chain);

        var result = await _handler.Handle(new GetCategoriesQuery(), CancellationToken.None);

        var node = result.Value!.Single();
        var names = new List<string> { node.Name };
        while (node.ChildCategories is [var only])
        {
            node = only;
            names.Add(node.Name);
        }

        Assert.That(names, Is.EqualTo(new[] { "L1", "L2", "L3", "L4", "L5" }));
        Assert.That(node.ChildCategories, Is.Empty, "the deepest category is a real leaf");
    }

    /// <summary>F-37. The old read took 100 roots and dropped the rest without a sign.</summary>
    [Test]
    public async Task Handle_ReturnsEveryRoot_NotJustTheFirstHundred()
    {
        RepositoryReturns(Enumerable.Range(0, 150)
            .Select(i => Category.Create($"Root {i:D3}", $"root-{i:D3}", null))
            .ToList());

        var result = await _handler.Handle(new GetCategoriesQuery(), CancellationToken.None);

        Assert.That(result.Value, Has.Count.EqualTo(150));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Handle_ReadsWithTheQuerysVisibility(bool includeInactive)
    {
        RepositoryReturns([]);

        await _handler.Handle(new GetCategoriesQuery { IncludeInactive = includeInactive }, CancellationToken.None);

        _categoryRepositoryMock.Verify(x => x.GetAllAsync(includeInactive, It.IsAny<CancellationToken>()), Times.Once);
    }
}
