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
        Assert.That(category.DisplayOrder, Is.Zero);
    }

    [Test]
    public void Create_WithCustomSlug_ShouldUseProvidedSlug()
    {
        // Act
        var category = Category.Create("Electronics & Gadgets", "electronics-gadgets", null);

        // Assert
        Assert.That(category.Slug, Is.EqualTo("electronics-gadgets"));
    }

    /// <summary>
    /// Stage 8: the parent is fixed at creation. The child also joins the parent's children, so the
    /// aggregate is consistent in memory before EF ever sees it.
    /// </summary>
    [Test]
    public void Create_WithParent_SetsTheParentAndJoinsItsChildren()
    {
        var parent = Category.Create("Electronics", null, null);

        var child = Category.Create("Laptops", null, parent);

        Assert.That(child.ParentCategoryId, Is.EqualTo(parent.Id));
        Assert.That(child.ParentCategory, Is.SameAs(parent));
        Assert.That(parent.ChildCategories, Is.EquivalentTo(new[] { child }));
    }

    [Test]
    public void Create_UnderADeletedParent_ShouldThrowDomainException()
    {
        var parent = Category.Create("Electronics", null, null);
        parent.Deactivate();

        Assert.Throws<DomainException>(() => Category.Create("Laptops", null, parent));
        Assert.That(parent.ChildCategories, Is.Empty);
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

    [Test]
    public void Create_WithNegativeDisplayOrder_ShouldThrowDomainException()
    {
        Assert.Throws<DomainException>(() => Category.Create("Electronics", null, null, displayOrder: -1));
    }

    /// <summary>M11 — create could set neither before Stage 8.</summary>
    [Test]
    public void Create_StoresTheDescriptionTrimmedAndTheDisplayOrder()
    {
        var category = Category.Create("Electronics", null, null, "  Gadgets and more  ", displayOrder: 3);

        Assert.That(category.Description, Is.EqualTo("Gadgets and more"));
        Assert.That(category.DisplayOrder, Is.EqualTo(3));
    }

    [Test]
    public void Create_WithABlankDescription_StoresNull()
    {
        var category = Category.Create("Electronics", null, null, "   ");

        Assert.That(category.Description, Is.Null);
    }

    /// <summary>M9 — a whitespace slug used to be trimmed to "" and stored.</summary>
    [Test]
    public void Create_WithABlankSlug_DerivesItFromTheName()
    {
        var category = Category.Create("Home And Garden", "   ", null);

        Assert.That(category.Slug, Is.EqualTo("home-and-garden"));
    }

    /// <summary>
    /// M9 — a name with no Latin letters or digits used to derive an empty slug, and the second
    /// such root collided on the unique root-slug index as a generic 409.
    /// </summary>
    [Test]
    public void Create_FromANameWithNoLatinLetters_FallsBackToAnIdBasedSlug()
    {
        var first = Category.Create("Книги", null, null);
        var second = Category.Create("Книги", null, null);

        Assert.That(first.Slug, Does.Match("^category-[0-9a-f]{8}$"));
        Assert.That(first.Slug, Does.EndWith(first.Id.ToString("N")[..8]));
        Assert.That(second.Slug, Is.Not.EqualTo(first.Slug));
    }

    #endregion

    #region Slugs

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

    [Test]
    public void GenerateSlug_TrimsDashesLeftByRemovedCharacters()
    {
        Assert.That(Category.GenerateSlug("Книги 2024"), Is.EqualTo("2024"));
    }

    /// <summary>
    /// The handler checks uniqueness against this before calling Create, so the two must agree —
    /// and a fallback (null) must mean "nothing to check".
    /// </summary>
    [TestCase("custom-slug", "Anything", ExpectedResult = "custom-slug")]
    [TestCase("  padded  ", "Anything", ExpectedResult = "padded")]
    [TestCase(null, "Home And Garden", ExpectedResult = "home-and-garden")]
    [TestCase("  ", "Home And Garden", ExpectedResult = "home-and-garden")]
    [TestCase(null, "Книги", ExpectedResult = null)]
    public string? ResolveRequestedSlug_AgreesWithCreate(string? slug, string name)
    {
        var resolved = Category.ResolveRequestedSlug(slug, name);

        if (resolved is not null)
        {
            Assert.That(Category.Create(name, slug, null).Slug, Is.EqualTo(resolved));
        }

        return resolved;
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

    /// <summary>M10 — the BUG-09 shape. An omitted description used to wipe the stored one.</summary>
    [Test]
    public void UpdateCategory_WithANullDescription_LeavesTheStoredOne()
    {
        var category = Category.Create("Electronics", null, null, "Keep me");

        category.UpdateCategory("Renamed", null);

        Assert.That(category.Description, Is.EqualTo("Keep me"));
    }

    [Test]
    public void UpdateCategory_WithABlankDescription_ClearsIt()
    {
        var category = Category.Create("Electronics", null, null, "Remove me");

        category.UpdateCategory("Electronics", "");

        Assert.That(category.Description, Is.Null);
    }

    [Test]
    public void UpdateCategory_WithoutADisplayOrder_LeavesIt_AndWithOneSetsIt()
    {
        var category = Category.Create("Electronics", null, null, displayOrder: 4);

        category.UpdateCategory("Electronics", null);
        Assert.That(category.DisplayOrder, Is.EqualTo(4));

        category.UpdateCategory("Electronics", null, displayOrder: 9);
        Assert.That(category.DisplayOrder, Is.EqualTo(9));
    }

    [Test]
    public void UpdateCategory_WithANegativeDisplayOrder_ShouldThrowDomainException()
    {
        var category = Category.Create("Electronics", null, null);

        Assert.Throws<DomainException>(() => category.UpdateCategory("Electronics", null, displayOrder: -1));
    }

    #endregion

    #region Deactivate

    /// <summary>M12 — nothing could set IsActive false before Stage 8, so its query filter was inert.</summary>
    [Test]
    public void Deactivate_ClearsIsActive_AndIsIdempotent()
    {
        var category = Category.Create("Electronics", null, null);

        category.Deactivate();
        category.Deactivate();

        Assert.That(category.IsActive, Is.False);
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
