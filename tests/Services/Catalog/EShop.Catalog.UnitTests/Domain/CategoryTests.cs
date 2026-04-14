using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.UnitTests.Domain;

[TestFixture]
public class CategoryTests
{
    #region Create

    [Test]
    public void Create_WithValidParameters_ShouldCreateCategory()
    {
        // Act
        var category = Category.Create("Electronics", null, null);

        // Assert
        Assert.That(category, Is.Not.Null);
        Assert.That(category.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(category.Name, Is.EqualTo("Electronics"));
        Assert.That(category.Slug, Is.EqualTo("electronics"));
        Assert.That(category.ParentCategoryId, Is.Null);
        Assert.That(category.IsActive, Is.True);
    }

    [Test]
    public void Create_WithCustomSlug_ShouldUseProvidedSlug()
    {
        // Act
        var category = Category.Create("Electronics & Gadgets", "electronics-gadgets", null);

        // Assert
        Assert.That(category.Slug, Is.EqualTo("electronics-gadgets"));
    }

    [Test]
    public void Create_WithParentCategoryId_ShouldSetParent()
    {
        // Arrange
        var parentId = Guid.NewGuid();

        // Act
        var category = Category.Create("Laptops", null, parentId);

        // Assert
        Assert.That(category.ParentCategoryId, Is.EqualTo(parentId));
    }

    [Test]
    public void Create_WithEmptyParentCategoryId_ShouldThrowDomainException()
    {
        Assert.Throws<DomainException>(() => Category.Create("Laptops", null, Guid.Empty));
    }

    [Test]
    public void Create_WithEmptyName_ShouldThrowDomainException()
    {
        // Act & Assert
        Assert.Throws<DomainException>(() => Category.Create("", null, null));
    }

    [Test]
    public void Create_WithWhitespaceName_ShouldThrowDomainException()
    {
        // Act & Assert
        Assert.Throws<DomainException>(() => Category.Create("   ", null, null));
    }

    [Test]
    public void Create_WithNullName_ShouldThrowDomainException()
    {
        // Act & Assert
        Assert.Throws<DomainException>(() => Category.Create(null!, null, null));
    }

    #endregion

    #region GenerateSlug

    [Test]
    public void GenerateSlug_SimpleText_ShouldReturnLowercaseSlug()
    {
        // Act
        var slug = Category.GenerateSlug("Electronics");

        // Assert
        Assert.That(slug, Is.EqualTo("electronics"));
    }

    [Test]
    public void GenerateSlug_TextWithSpaces_ShouldReplaceDashes()
    {
        // Act
        var slug = Category.GenerateSlug("Home And Garden");

        // Assert
        Assert.That(slug, Is.EqualTo("home-and-garden"));
    }

    [Test]
    public void GenerateSlug_TextWithSpecialChars_ShouldRemoveSpecialChars()
    {
        // Act
        var slug = Category.GenerateSlug("Electronics & Gadgets!");

        // Assert — regex removes '&' and '!', collapses multiple dashes
        Assert.That(slug, Is.EqualTo("electronics-gadgets"));
    }

    [Test]
    public void GenerateSlug_TextWithMultipleSpaces_ShouldCollapseToDash()
    {
        // Act
        var slug = Category.GenerateSlug("Home   Garden");

        // Assert
        Assert.That(slug, Is.EqualTo("home-garden"));
    }

    #endregion

    #region AddChildCategory

    [Test]
    public void AddChildCategory_WithValidName_ShouldAddChild()
    {
        // Arrange
        var parent = Category.Create("Electronics", null, null);

        // Act
        parent.AddChildCategory("Laptops");

        // Assert
        Assert.That(parent.ChildCategories, Has.Count.EqualTo(1));
        Assert.That(parent.ChildCategories.First().Name, Is.EqualTo("Laptops"));
        Assert.That(parent.ChildCategories.First().ParentCategoryId, Is.EqualTo(parent.Id));
    }

    [Test]
    public void AddChildCategory_WithCustomSlug_ShouldUseSlug()
    {
        // Arrange
        var parent = Category.Create("Electronics", null, null);

        // Act
        parent.AddChildCategory("Laptops & Notebooks", "laptops-notebooks");

        // Assert
        Assert.That(parent.ChildCategories.First().Slug, Is.EqualTo("laptops-notebooks"));
    }

    [Test]
    public void SetParent_WithSelf_ShouldThrowDomainException()
    {
        var category = Category.Create("Electronics", null, null);

        Assert.Throws<DomainException>(() => category.SetParent(category));
    }

    [Test]
    public void SetParent_WhenWouldCreateCycle_ShouldThrowDomainException()
    {
        var root = Category.Create("Root", null, null);
        var child = Category.Create("Child", null, null);
        child.SetParent(root);

        Assert.Throws<DomainException>(() => root.SetParent(child));
    }

    [Test]
    public void AddChildCategory_WithEmptyName_ShouldThrowDomainException()
    {
        // Arrange
        var parent = Category.Create("Electronics", null, null);

        // Act & Assert
        Assert.Throws<DomainException>(() => parent.AddChildCategory(""));
    }

    #endregion

    #region UpdateCategory

    [Test]
    public void UpdateCategory_WithValidParameters_ShouldUpdate()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);

        // Act
        category.UpdateCategory("Updated Electronics", "A description");

        // Assert
        Assert.That(category.Name, Is.EqualTo("Updated Electronics"));
        Assert.That(category.Description, Is.EqualTo("A description"));
    }

    [Test]
    public void UpdateCategory_WithEmptyName_ShouldThrowDomainException()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);

        // Act & Assert
        Assert.Throws<DomainException>(() => category.UpdateCategory("", "A description"));
    }

    [Test]
    public void UpdateCategory_TrimsWhitespace_ShouldTrimNameAndDescription()
    {
        // Arrange
        var category = Category.Create("Electronics", null, null);

        // Act
        category.UpdateCategory("  Updated  ", "  Description  ");

        // Assert
        Assert.That(category.Name, Is.EqualTo("Updated"));
        Assert.That(category.Description, Is.EqualTo("Description"));
    }

    #endregion

    #region Version

    [Test]
    public void Version_NewCategory_ShouldBeZero()
    {
        // Act
        var category = Category.Create("Test", null, null);

        // Assert
        Assert.That(category.Version, Is.EqualTo(0));
    }

    [Test]
    public void IncrementVersion_ShouldIncreaseByOne()
    {
        // Arrange
        var category = Category.Create("Test", null, null);

        // Act
        category.IncrementVersion();

        // Assert
        Assert.That(category.Version, Is.EqualTo(1));
    }

    #endregion
}
