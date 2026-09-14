using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Categories.Queries.GetCategories;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Mapster;
using MapsterMapper;
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

        // A real mapper over the application's own registrations, not a mock: the handler's whole
        // job is the mapping, and a mocked IMapper would only echo back whatever it was told.
        var config = new TypeAdapterConfig();
        config.Scan(typeof(CategoryDto).Assembly);
        _handler = new GetCategoriesQueryHandler(_categoryRepositoryMock.Object, new Mapper(config));
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
