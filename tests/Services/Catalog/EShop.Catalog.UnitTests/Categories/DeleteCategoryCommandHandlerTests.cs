using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Categories.Commands.DeleteCategory;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Categories;

[TestFixture]
public class DeleteCategoryCommandHandlerTests
{
    private Mock<ICategoryRepository> _categoryRepositoryMock = null!;
    private Mock<IProductRepository> _productRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private DeleteCategoryCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _categoryRepositoryMock = new Mock<ICategoryRepository>();
        _productRepositoryMock = new Mock<IProductRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _handler = new DeleteCategoryCommandHandler(
            _categoryRepositoryMock.Object,
            _productRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Test]
    public async Task Handle_WithEmptyCategory_ShouldDeleteAndReturnSuccess()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);
        var command = new DeleteCategoryCommand { Id = category.Id };

        _categoryRepositoryMock
            .Setup(x => x.GetById(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        _productRepositoryMock
            .Setup(x => x.GetByCategoryAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<Product>());

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _categoryRepositoryMock.Verify(x => x.DeleteAsync(category, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentCategory_ShouldReturnNotFoundError()
    {
        // Arrange
        var command = new DeleteCategoryCommand { Id = Guid.NewGuid() };

        _categoryRepositoryMock
            .Setup(x => x.GetById(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.NotFound"));
    }

    [Test]
    public async Task Handle_WithCategoryHavingChildren_ShouldReturnHasChildrenError()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);
        category.AddChildCategory("Laptops");
        var command = new DeleteCategoryCommand { Id = category.Id };

        _categoryRepositoryMock
            .Setup(x => x.GetById(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.HasChildren"));
        _categoryRepositoryMock.Verify(x => x.DeleteAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithCategoryHavingProducts_ShouldReturnHasProductsError()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);
        var command = new DeleteCategoryCommand { Id = category.Id };

        _categoryRepositoryMock
            .Setup(x => x.GetById(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var products = new[]
        {
            Product.Create("Product 1", "SKU-001", 29.99m, 10, category.Id)
        };

        _productRepositoryMock
            .Setup(x => x.GetByCategoryAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.HasProducts"));
        _categoryRepositoryMock.Verify(x => x.DeleteAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
