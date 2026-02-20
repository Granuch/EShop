using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Categories.Commands.UpdateCategory;
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
        _handler = new UpdateCategoryCommandHandler(
            _repositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Test]
    public async Task Handle_WithExistingCategory_ShouldUpdateAndReturnSuccess()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);
        var command = new UpdateCategoryCommand
        {
            Id = category.Id,
            Name = "Updated Electronics",
            Description = "Updated description"
        };

        _repositoryMock
            .Setup(x => x.GetById(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

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
}
