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
    private Mock<ICacheInvalidationContext> _cacheInvalidationContextMock = null!;
    private List<string> _evicted = null!;
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

        _evicted = [];
        _cacheInvalidationContextMock = new Mock<ICacheInvalidationContext>();
        _cacheInvalidationContextMock
            .Setup(x => x.AddKeys(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(keys => _evicted.AddRange(keys));

        _handler = new DeleteCategoryCommandHandler(
            _categoryRepositoryMock.Object,
            _productRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cacheInvalidationContextMock.Object);
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

    /// <summary>M8 — the parent's cached detail still lists a deleted child until its entry goes.</summary>
    [Test]
    public async Task Handle_EvictsTheParentsCachedDetail()
    {
        var parent = Category.Create("Parent", null, null);
        var category = Category.Create("Child", null, parent);
        Returns(category);

        await _handler.Handle(new DeleteCategoryCommand { Id = category.Id }, CancellationToken.None);

        Assert.That(_evicted, Is.EquivalentTo(new[] { CategoryCacheKeys.Detail(parent.Id) }));
    }
}
