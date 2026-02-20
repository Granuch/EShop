using EShop.Catalog.Application.Categories.Queries.GetCategoryById;
using EShop.Catalog.Application.Categories;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using MapsterMapper;
using Moq;

namespace EShop.Catalog.UnitTests.Categories;

[TestFixture]
public class GetCategoryByIdQueryHandlerTests
{
    private Mock<ICategoryRepository> _categoryRepositoryMock = null!;
    private Mock<IMapper> _mapperMock = null!;
    private GetCategoryByIdQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _categoryRepositoryMock = new Mock<ICategoryRepository>();
        _mapperMock = new Mock<IMapper>();
        _handler = new GetCategoryByIdQueryHandler(
            _categoryRepositoryMock.Object,
            _mapperMock.Object);
    }

    [Test]
    public async Task Handle_WithExistingCategory_ShouldReturnCategoryDto()
    {
        // Arrange
        var category = Category.Create("Electronics", "electronics", null);
        var query = new GetCategoryByIdQuery { Id = category.Id };

        var expectedDto = new CategoryDto
        {
            Id = category.Id,
            Name = "Electronics",
            Slug = "electronics",
            IsActive = true
        };

        _categoryRepositoryMock
            .Setup(x => x.GetById(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        _mapperMock
            .Setup(x => x.Map<CategoryDto>(category))
            .Returns(expectedDto);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Id, Is.EqualTo(category.Id));
        Assert.That(result.Value.Name, Is.EqualTo("Electronics"));
    }

    [Test]
    public async Task Handle_WithNonExistentCategory_ShouldReturnNotFoundError()
    {
        // Arrange
        var query = new GetCategoryByIdQuery { Id = Guid.NewGuid() };

        _categoryRepositoryMock
            .Setup(x => x.GetById(query.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.NotFound"));
    }
}
