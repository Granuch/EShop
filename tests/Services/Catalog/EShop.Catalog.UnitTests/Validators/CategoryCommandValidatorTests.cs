using EShop.Catalog.Application.Categories.Commands.CreateCategory;
using EShop.Catalog.Application.Categories.Commands.DeleteCategory;
using EShop.Catalog.Application.Categories.Commands.UpdateCategory;
using EShop.Catalog.Domain.Entities;
using FluentValidation.TestHelper;

namespace EShop.Catalog.UnitTests.Validators;

[TestFixture]
public class CategoryCommandValidatorTests
{
    private CreateCategoryCommandValidator _createValidator = null!;
    private UpdateCategoryCommandValidator _updateValidator = null!;
    private DeleteCategoryCommandValidator _deleteValidator = null!;

    [SetUp]
    public void SetUp()
    {
        _createValidator = new CreateCategoryCommandValidator();
        _updateValidator = new UpdateCategoryCommandValidator();
        _deleteValidator = new DeleteCategoryCommandValidator();
    }

    #region CreateCategoryCommandValidator

    [Test]
    public void CreateCategory_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new CreateCategoryCommand
        {
            Name = "Electronics",
            Slug = "electronics"
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void CreateCategory_EmptyName_ShouldHaveError()
    {
        var command = new CreateCategoryCommand { Name = "" };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    public void CreateCategory_NameExceeds200Characters_ShouldHaveError()
    {
        var command = new CreateCategoryCommand { Name = new string('x', 201) };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    public void CreateCategory_SlugExceeds200Characters_ShouldHaveError()
    {
        var command = new CreateCategoryCommand
        {
            Name = "Valid Name",
            Slug = new string('x', 201)
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Slug);
    }

    [Test]
    public void CreateCategory_NullSlug_ShouldNotHaveSlugError()
    {
        var command = new CreateCategoryCommand
        {
            Name = "Electronics",
            Slug = null
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Slug);
    }

    /// <summary>
    /// F-39 (frontend-contracts R5). A supplied slug used to be only trimmed, so
    /// "  Mixed Case Slug!! " was stored as "Mixed Case Slug!!". The rule is the shape GenerateSlug
    /// produces, checked as sent.
    /// </summary>
    [TestCase("Mixed Case Slug!!")]
    [TestCase("Electronics")]
    [TestCase(" padded ")]
    [TestCase("under_score")]
    [TestCase("-leading")]
    [TestCase("trailing-")]
    [TestCase("double--dash")]
    [TestCase("ünïcode")]
    public void CreateCategory_MalformedSlug_ShouldHaveError(string slug)
    {
        var result = _createValidator.TestValidate(new CreateCategoryCommand { Name = "Valid Name", Slug = slug });

        result.ShouldHaveValidationErrorFor(x => x.Slug).WithErrorMessage(Category.InvalidSlugMessage);
    }

    [TestCase("electronics")]
    [TestCase("home-and-garden")]
    [TestCase("2024")]
    [TestCase("a")]
    public void CreateCategory_WellFormedSlug_ShouldNotHaveSlugError(string slug)
        => _createValidator.TestValidate(new CreateCategoryCommand { Name = "Valid Name", Slug = slug })
            .ShouldNotHaveValidationErrorFor(x => x.Slug);

    /// <summary>Blank still means "derive it from the name", as the domain reads it.</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void CreateCategory_BlankSlug_ShouldNotHaveSlugError(string slug)
        => _createValidator.TestValidate(new CreateCategoryCommand { Name = "Valid Name", Slug = slug })
            .ShouldNotHaveValidationErrorFor(x => x.Slug);

    [Test]
    public void CreateCategory_SlugOfExactly200Characters_ShouldNotHaveSlugError()
        => _createValidator.TestValidate(new CreateCategoryCommand { Name = "Valid Name", Slug = new string('x', 200) })
            .ShouldNotHaveValidationErrorFor(x => x.Slug);

    #endregion

    #region UpdateCategoryCommandValidator

    /// <summary>F-39. PUT accepts a slug since R5: omitted leaves it, anything sent must be valid.</summary>
    [Test]
    public void UpdateCategory_WithoutASlug_ShouldNotHaveSlugError()
        => _updateValidator.TestValidate(new UpdateCategoryCommand { Id = Guid.NewGuid(), Name = "N" })
            .ShouldNotHaveValidationErrorFor(x => x.Slug);

    [Test]
    public void UpdateCategory_WellFormedSlug_ShouldNotHaveSlugError()
        => _updateValidator.TestValidate(new UpdateCategoryCommand { Id = Guid.NewGuid(), Name = "N", Slug = "new-slug" })
            .ShouldNotHaveValidationErrorFor(x => x.Slug);

    /// <summary>
    /// Blank is refused here, unlike on create: a category always has a slug, so "" cannot mean
    /// "clear it", and reading it as "leave it" would hide a client bug.
    /// </summary>
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Mixed Case")]
    [TestCase("bad!")]
    public void UpdateCategory_BlankOrMalformedSlug_ShouldHaveError(string slug)
        => _updateValidator.TestValidate(new UpdateCategoryCommand { Id = Guid.NewGuid(), Name = "N", Slug = slug })
            .ShouldHaveValidationErrorFor(x => x.Slug);

    [Test]
    public void UpdateCategory_SlugExceeds200Characters_ShouldHaveError()
        => _updateValidator.TestValidate(new UpdateCategoryCommand { Id = Guid.NewGuid(), Name = "N", Slug = new string('x', 201) })
            .ShouldHaveValidationErrorFor(x => x.Slug);

    [Test]
    public void UpdateCategory_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new UpdateCategoryCommand
        {
            Id = Guid.NewGuid(),
            Name = "Updated Name",
            Description = "A description"
        };

        var result = _updateValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void UpdateCategory_EmptyId_ShouldHaveError()
    {
        var command = new UpdateCategoryCommand
        {
            Id = Guid.Empty,
            Name = "Updated",
            Description = "Desc"
        };

        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Test]
    public void UpdateCategory_EmptyName_ShouldHaveError()
    {
        var command = new UpdateCategoryCommand
        {
            Id = Guid.NewGuid(),
            Name = "",
            Description = "Desc"
        };

        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    public void UpdateCategory_NameExceeds200Characters_ShouldHaveError()
    {
        var command = new UpdateCategoryCommand
        {
            Id = Guid.NewGuid(),
            Name = new string('x', 201),
            Description = "Desc"
        };

        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    public void UpdateCategory_DescriptionExceeds1000Characters_ShouldHaveError()
    {
        var command = new UpdateCategoryCommand
        {
            Id = Guid.NewGuid(),
            Name = "Valid Name",
            Description = new string('x', 1001)
        };

        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    /// <summary>
    /// M10. Omitting the description must validate, or the "omitted leaves it" contract is
    /// unreachable over HTTP.
    /// </summary>
    [Test]
    public void UpdateCategory_WithoutADescription_ShouldHaveNoErrors()
    {
        var command = new UpdateCategoryCommand { Id = Guid.NewGuid(), Name = "Name only" };

        _updateValidator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void UpdateCategory_NegativeDisplayOrder_ShouldHaveError()
    {
        var command = new UpdateCategoryCommand { Id = Guid.NewGuid(), Name = "N", DisplayOrder = -1 };

        _updateValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.DisplayOrder);
    }

    [Test]
    public void CreateCategory_DescriptionExceeds1000Characters_ShouldHaveError()
    {
        var command = new CreateCategoryCommand { Name = "N", Description = new string('x', 1001) };

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Test]
    public void CreateCategory_NegativeDisplayOrder_ShouldHaveError()
    {
        var command = new CreateCategoryCommand { Name = "N", DisplayOrder = -1 };

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.DisplayOrder);
    }

    #endregion

    #region DeleteCategoryCommandValidator

    [Test]
    public void DeleteCategory_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new DeleteCategoryCommand { Id = Guid.NewGuid() };

        var result = _deleteValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void DeleteCategory_EmptyId_ShouldHaveError()
    {
        var command = new DeleteCategoryCommand { Id = Guid.Empty };

        var result = _deleteValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    #endregion
}
