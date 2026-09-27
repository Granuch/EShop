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
    private UpdateCategoryCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<ICategoryRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _handler = new UpdateCategoryCommandHandler(_repositoryMock.Object, _unitOfWorkMock.Object);
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

    /// <summary>F-39. Omitted leaves the slug alone, and asks nothing of the repository.</summary>
    [Test]
    public async Task Handle_WithoutASlug_KeepsTheStoredOne()
    {
        var category = Category.Create("Electronics", "electronics", null);
        Returns(category);

        await _handler.Handle(new UpdateCategoryCommand { Id = category.Id, Name = "Renamed" }, CancellationToken.None);

        Assert.That(category.Slug, Is.EqualTo("electronics"));
        _repositoryMock.Verify(x => x.SlugExistsAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>F-39. No endpoint could change a slug before R5.</summary>
    [Test]
    public async Task Handle_WithAFreeSlug_ChangesIt_CheckingTheCategorysOwnLevel()
    {
        var parent = Category.Create("Parent", "parent", null);
        var category = Category.Create("Electronics", "electronics", parent);
        Returns(category);

        var result = await _handler.Handle(
            new UpdateCategoryCommand { Id = category.Id, Name = "Electronics", Slug = "gadgets" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(category.Slug, Is.EqualTo("gadgets"));
        _repositoryMock.Verify(x => x.SlugExistsAsync(parent.Id, "gadgets", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// F-39. A taken slug is refused before anything is mutated: TransactionBehavior commits on a
    /// failure Result, so a rename applied before the check would be persisted by the refusal.
    /// </summary>
    [Test]
    public async Task Handle_WithATakenSlug_IsASlugConflict_AndChangesNothing()
    {
        var category = Category.Create("Electronics", "electronics", null);
        Returns(category);
        _repositoryMock
            .Setup(x => x.SlugExistsAsync(null, "taken", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(
            new UpdateCategoryCommand { Id = category.Id, Name = "Renamed", Slug = "taken" }, CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Category.SlugConflict"));
        Assert.That(category.Slug, Is.EqualTo("electronics"));
        Assert.That(category.Name, Is.EqualTo("Electronics"), "the rename must not ride along with a refused request");
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>F-39. Re-sending the current slug must not collide with the category itself.</summary>
    [Test]
    public async Task Handle_ResendingItsOwnSlug_IsNotAConflict()
    {
        var category = Category.Create("Electronics", "electronics", null);
        Returns(category);
        _repositoryMock
            .Setup(x => x.SlugExistsAsync(It.IsAny<Guid?>(), "electronics", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(
            new UpdateCategoryCommand { Id = category.Id, Name = "Renamed", Slug = "electronics" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    /// <summary>
    /// The keys the commands evict must be the keys the queries write — and since F-37
    /// (frontend-contracts R5) both category reads are versioned in one family, so every category
    /// write must bump that family and must name <b>no</b> exact key: a versioned entry's stored key
    /// embeds the family version, so an exact-key eviction matches nothing and logs success.
    /// </summary>
    [Test]
    public void BothCategoryReadsAreVersioned_AndEveryCategoryWriteBumpsTheirFamily()
    {
        var id = Guid.NewGuid();

        // Assert the INTERFACE, not just the property: a record that dropped IVersionedCacheKey but
        // kept CacheKeyFamily would satisfy a property-only check while caching unversioned — and
        // for the detail read that means a rename three levels down stays stale for five minutes.
        var detailQuery = new GetCategoryByIdQuery { Id = id };
        Assert.That(detailQuery, Is.InstanceOf<IVersionedCacheKey>());
        Assert.That(detailQuery.CacheKeyFamily, Is.EqualTo(CategoryCacheFamilies.CategoryList));
        Assert.That(detailQuery.CacheKey, Is.EqualTo(CategoryCacheKeys.Detail(id)));

        var listQuery = new GetCategoriesQuery();
        Assert.That(listQuery, Is.InstanceOf<IVersionedCacheKey>());
        Assert.That(listQuery.CacheKeyFamily, Is.EqualTo(CategoryCacheFamilies.CategoryList));

        ICacheInvalidatingCommand[] writes =
        [
            new EShop.Catalog.Application.Categories.Commands.CreateCategory.CreateCategoryCommand { Name = "N", ParentCategoryId = id },
            new UpdateCategoryCommand { Id = id, Name = "N" },
            new EShop.Catalog.Application.Categories.Commands.DeleteCategory.DeleteCategoryCommand { Id = id },
            new EShop.Catalog.Application.Categories.Commands.MoveCategory.MoveCategoryCommand { CategoryId = id, NewParentCategoryId = Guid.NewGuid() },
            new EShop.Catalog.Application.Categories.Commands.ReorderCategories.ReorderCategoriesCommand { ParentCategoryId = id, CategoryIds = [id] },
            new EShop.Catalog.Application.Categories.Commands.RestoreCategory.RestoreCategoryCommand { CategoryId = id }
        ];

        foreach (var write in writes)
        {
            Assert.That(write.CacheFamiliesToInvalidate, Contains.Item(CategoryCacheFamilies.CategoryList), write.GetType().Name);
            Assert.That(write.CacheKeysToInvalidate, Is.Empty, write.GetType().Name);
        }

        // The two list variants must be distinct keys, or an admin request poisons the anonymous entry.
        Assert.That(new GetCategoriesQuery { IncludeInactive = true }.CacheKey,
            Is.Not.EqualTo(new GetCategoriesQuery { IncludeInactive = false }.CacheKey));
    }
}
