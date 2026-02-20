using EShop.Catalog.Application.Categories;
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

    [Test]
    public async Task Handle_ShouldReturnAllRootCategories()
    {
        // Arrange
        var categories = new List<Category>
        {
            Category.Create("Electronics", "electronics", null),
            Category.Create("Clothing", "clothing", null)
        };

        _categoryRepositoryMock
            .Setup(x => x.GetRootCategories(It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);

        var query = new GetCategoriesQuery();

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Handle_WithNoCategories_ShouldReturnEmptyList()
    {
        // Arrange
        _categoryRepositoryMock
            .Setup(x => x.GetRootCategories(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Category>());

        var query = new GetCategoriesQuery();

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(0));
    }
}
