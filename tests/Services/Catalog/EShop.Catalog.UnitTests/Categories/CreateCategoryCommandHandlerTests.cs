using EShop.BuildingBlocks.Domain;
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

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

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
        var parentId = parent.Id;

        var command = new CreateCategoryCommand
        {
            Name = "Laptops",
            ParentCategoryId = parentId
        };

        _categoryRepositoryMock
            .Setup(x => x.GetById(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _categoryRepositoryMock.Verify(
            x => x.AddAsync(
                It.Is<Category>(c => c.ParentCategoryId == parentId),
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

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

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
}
