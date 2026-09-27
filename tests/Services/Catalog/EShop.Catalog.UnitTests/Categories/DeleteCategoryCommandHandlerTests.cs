using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Categories;
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
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _handler = new DeleteCategoryCommandHandler(
            _categoryRepositoryMock.Object,
            _productRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    private void Returns(Category category)
        => _categoryRepositoryMock
            .Setup(x => x.GetById(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

    /// <summary>M12 — soft, not a hard Remove.</summary>
    [Test]
    public async Task Handle_WithEmptyCategory_DeactivatesItAndReturnsSuccess()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);
        Returns(category);

        // Act
        var result = await _handler.Handle(new DeleteCategoryCommand { Id = category.Id }, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(category.IsActive, Is.False);
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
        Category.Create("Laptops", null, category);
        Returns(category);

        // Act
        var result = await _handler.Handle(new DeleteCategoryCommand { Id = category.Id }, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.HasChildren"));
        Assert.That(category.IsActive, Is.True);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithCategoryHavingProducts_ShouldReturnHasProductsError()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);
        Returns(category);

        _productRepositoryMock
            .Setup(x => x.AnyInCategoryAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(new DeleteCategoryCommand { Id = category.Id }, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.HasProducts"));
        Assert.That(category.IsActive, Is.True);
    }

    /// <summary>
    /// M8, reworked in F-37 (frontend-contracts R5). Every ancestor's cached detail lists a deleted
    /// descendant until its entry goes, and no exact-key list can name them all, so the command bumps
    /// the family both category reads are versioned in — and names no exact key, which would match
    /// no stored entry.
    /// </summary>
    [Test]
    public void TheCommand_BumpsTheCategoryFamily_AndNamesNoExactKey()
    {
        var command = new DeleteCategoryCommand { Id = Guid.NewGuid() };

        Assert.That(command.CacheFamiliesToInvalidate, Is.EqualTo(new[] { CategoryCacheFamilies.CategoryList }));
        Assert.That(command.CacheKeysToInvalidate, Is.Empty);
    }
}
