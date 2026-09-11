using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Categories.Commands.UpdateCategory;
using EShop.Catalog.Application.Categories.Queries.GetCategories;
using EShop.Catalog.Application.Categories.Queries.GetCategoryById;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Categories;

[TestFixture]
public class UpdateCategoryCommandHandlerTests
{
    private Mock<ICategoryRepository> _repositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<ICacheInvalidationContext> _cacheInvalidationContextMock = null!;
    private List<string> _evicted = null!;
    private UpdateCategoryCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<ICategoryRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _evicted = [];
        _cacheInvalidationContextMock = new Mock<ICacheInvalidationContext>();
        _cacheInvalidationContextMock
            .Setup(x => x.AddKeys(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(keys => _evicted.AddRange(keys));

        _handler = new UpdateCategoryCommandHandler(
            _repositoryMock.Object,
            _unitOfWorkMock.Object,
            _cacheInvalidationContextMock.Object);
    }

    private void Returns(Category category)
        => _repositoryMock
            .Setup(x => x.GetById(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

    [Test]
    public async Task Handle_WithExistingCategory_ShouldUpdateAndReturnSuccess()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);
        Returns(category);
        var command = new UpdateCategoryCommand
        {
            Id = category.Id,
            Name = "Updated Electronics",
            Description = "Updated description"
        };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(category.Name, Is.EqualTo("Updated Electronics"));
        Assert.That(category.Description, Is.EqualTo("Updated description"));
        _repositoryMock.Verify(x => x.UpdateAsync(category, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentCategory_ShouldReturnNotFoundError()
    {
        // Arrange
        var command = new UpdateCategoryCommand
        {
            Id = Guid.NewGuid(),
            Name = "Updated",
            Description = "Desc"
        };

        _repositoryMock
            .Setup(x => x.GetById(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.NotFound"));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(_evicted, Is.Empty);
    }

    /// <summary>M10 — a command that omits the description must not wipe it.</summary>
    [Test]
    public async Task Handle_WithoutADescription_KeepsTheStoredOne()
    {
        var category = Category.Create("Electronics", null, null, "Keep me");
        Returns(category);

        await _handler.Handle(new UpdateCategoryCommand { Id = category.Id, Name = "Renamed" }, CancellationToken.None);

        Assert.That(category.Description, Is.EqualTo("Keep me"));
    }

    /// <summary>
    /// M8. The parent's cached detail lists this category and each child's carries its name, so
    /// both must go — the command itself can only name the category's own key.
    /// </summary>
    [Test]
    public async Task Handle_EvictsTheParentsAndEachChildsCachedDetail()
    {
        var parent = Category.Create("Parent", null, null);
        var category = Category.Create("Middle", null, parent);
        var firstChild = Category.Create("Child A", null, category);
        var secondChild = Category.Create("Child B", null, category);
        Returns(category);

        await _handler.Handle(new UpdateCategoryCommand { Id = category.Id, Name = "Renamed" }, CancellationToken.None);

        Assert.That(_evicted, Is.EquivalentTo(new[]
        {
            CategoryCacheKeys.Detail(parent.Id),
            CategoryCacheKeys.Detail(firstChild.Id),
            CategoryCacheKeys.Detail(secondChild.Id)
        }));
    }

    /// <summary>
    /// The keys the commands evict must be the keys the queries write. They were separate string
    /// literals until Stage 8 — the Stage 6 lesson is that such pairs drift silently.
    /// </summary>
    [Test]
    public void TheQueriesCacheUnderTheKeysTheCommandsEvict()
    {
        var id = Guid.NewGuid();

        Assert.That(new GetCategoryByIdQuery { Id = id }.CacheKey, Is.EqualTo(CategoryCacheKeys.Detail(id)));
        Assert.That(new GetCategoriesQuery().CacheKey, Is.EqualTo(CategoryCacheKeys.All));
    }
}
