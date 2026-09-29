using EShop.Catalog.Application.Categories.Queries.GetCategoryById;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Categories;

[TestFixture]
public class GetCategoryByIdQueryHandlerTests
{
    private Mock<ICategoryRepository> _categoryRepositoryMock = null!;
    private GetCategoryByIdQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _categoryRepositoryMock = new Mock<ICategoryRepository>();
        _handler = new GetCategoryByIdQueryHandler(_categoryRepositoryMock.Object);
    }

    private void RepositoryReturns(params Category[] categories)
        => _categoryRepositoryMock
            .Setup(x => x.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories.ToList());

    [Test]
    public async Task Handle_WithExistingCategory_ShouldReturnCategoryDto()
    {
        var category = Category.Create("Electronics", "electronics", null);
        RepositoryReturns(category, Category.Create("Other", "other", null));

        var result = await _handler.Handle(new GetCategoryByIdQuery { Id = category.Id }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Id, Is.EqualTo(category.Id));
        Assert.That(result.Value.Name, Is.EqualTo("Electronics"));
        Assert.That(result.Value.Slug, Is.EqualTo("electronics"));
        Assert.That(result.Value.IsActive, Is.True);
    }

    /// <summary>
    /// F-37. The detail used to include one level of children, each with <c>childCategories: []</c>
    /// whatever lay below. Read from the middle of a four-level chain, so both the parent's name above
    /// and two levels below are in play.
    /// </summary>
    [Test]
    public async Task Handle_ReturnsTheWholeSubtree_AndTheParentsName()
    {
        var root = Category.Create("Root", "root", null);
        var middle = Category.Create("Middle", "middle", root);
        var child = Category.Create("Child", "child", middle);
        var grandchild = Category.Create("Grandchild", "grandchild", child);
        RepositoryReturns(root, middle, child, grandchild);

        var result = await _handler.Handle(new GetCategoryByIdQuery { Id = middle.Id }, CancellationToken.None);

        var dto = result.Value!;
        Assert.That(dto.ParentCategoryName, Is.EqualTo("Root"));
        Assert.That(dto.ChildCategories!.Single().Name, Is.EqualTo("Child"));
        Assert.That(dto.ChildCategories!.Single().ParentCategoryName, Is.EqualTo("Middle"));
        Assert.That(dto.ChildCategories!.Single().ChildCategories!.Single().Name, Is.EqualTo("Grandchild"));
    }

    [Test]
    public async Task Handle_WithNonExistentCategory_ShouldReturnNotFoundError()
    {
        RepositoryReturns(Category.Create("Other", "other", null));

        var result = await _handler.Handle(new GetCategoryByIdQuery { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.NotFound"));
    }
}
