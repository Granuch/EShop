using EShop.Catalog.Application.Categories.Commands.CreateCategory;
using EShop.Catalog.Application.Categories.Commands.DeleteCategory;
using EShop.Catalog.Application.Categories.Commands.UpdateCategory;
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

    #endregion

    #region UpdateCategoryCommandValidator

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
