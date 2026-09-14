using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Categories.Commands.CreateCategory;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Categories;

[TestFixture]
public class CreateCategoryCommandHandlerTests
{
    private Mock<ICategoryRepository> _categoryRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private CreateCategoryCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _categoryRepositoryMock = new Mock<ICategoryRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _handler = new CreateCategoryCommandHandler(
            _categoryRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldReturnSuccessWithCategoryId()
    {
        // Arrange
        var command = new CreateCategoryCommand
        {
            Name = "Electronics",
            Slug = "electronics"
        };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.EqualTo(Guid.Empty));
        _categoryRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithParentCategoryId_ShouldCreateChildCategory()
    {
        // Arrange
        var parent = Category.Create("Parent", "parent", null);

        var command = new CreateCategoryCommand
        {
            Name = "Laptops",
            ParentCategoryId = parent.Id
        };

        _categoryRepositoryMock
            .Setup(x => x.GetById(parent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _categoryRepositoryMock.Verify(
            x => x.AddAsync(
                It.Is<Category>(c => c.ParentCategoryId == parent.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_WithUnknownParentCategoryId_ShouldReturnFailure()
    {
        var parentId = Guid.NewGuid();
        var command = new CreateCategoryCommand
        {
            Name = "Laptops",
            ParentCategoryId = parentId
        };

        _categoryRepositoryMock
            .Setup(x => x.GetById(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.ParentNotFound"));
        _categoryRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithNullSlug_ShouldAutoGenerateSlug()
    {
        // Arrange
        var command = new CreateCategoryCommand
        {
            Name = "Home And Garden",
            Slug = null
        };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _categoryRepositoryMock.Verify(
            x => x.AddAsync(
                It.Is<Category>(c => c.Slug == "home-and-garden"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_PassesTheDescriptionAndDisplayOrderThrough()
    {
        var command = new CreateCategoryCommand { Name = "Books", Description = "Paper", DisplayOrder = 5 };

        await _handler.Handle(command, CancellationToken.None);

        _categoryRepositoryMock.Verify(
            x => x.AddAsync(
                It.Is<Category>(c => c.Description == "Paper" && c.DisplayOrder == 5),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// M9. The conflict must be detected before anything is built: Category.Create attaches the new
    /// category to its tracked parent, and TransactionBehavior commits even on a failure Result, so
    /// a late check would still insert the rejected category through the parent.
    /// </summary>
    [Test]
    public async Task Handle_WithASlugTakenAtThatLevel_FailsWithoutTouchingTheParent()
    {
        var parent = Category.Create("Parent", "parent", null);
        _categoryRepositoryMock
            .Setup(x => x.GetById(parent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);
        _categoryRepositoryMock
            .Setup(x => x.SlugExistsAsync(parent.Id, "taken", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(
            new CreateCategoryCommand { Name = "Dup", Slug = "taken", ParentCategoryId = parent.Id },
            CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.SlugConflict"));
        Assert.That(parent.ChildCategories, Is.Empty,
            "the rejected category must never reach the tracked parent's children");
        _categoryRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_ChecksTheGeneratedSlug_WhenNoneIsSupplied()
    {
        _categoryRepositoryMock
            .Setup(x => x.SlugExistsAsync(null, "home-and-garden", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(new CreateCategoryCommand { Name = "Home And Garden" }, CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("Category.SlugConflict"));
    }

    /// <summary>An id-based fallback slug cannot collide, so it is not looked up at all.</summary>
    [Test]
    public async Task Handle_DoesNotCheckAFallbackSlug()
    {
        var result = await _handler.Handle(new CreateCategoryCommand { Name = "Книги" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _categoryRepositoryMock.Verify(
            x => x.SlugExistsAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>M8. A new child must evict its parent's cached detail.</summary>
    [Test]
    public void CacheKeysToInvalidate_IncludeTheParentsDetail()
    {
        var parentId = Guid.NewGuid();

        Assert.That(new CreateCategoryCommand { Name = "C", ParentCategoryId = parentId }.CacheKeysToInvalidate,
            Is.EquivalentTo(new[] { CategoryCacheKeys.All, CategoryCacheKeys.Detail(parentId) }));
        Assert.That(new CreateCategoryCommand { Name = "Root" }.CacheKeysToInvalidate,
            Is.EquivalentTo(new[] { CategoryCacheKeys.All }));
    }
}
