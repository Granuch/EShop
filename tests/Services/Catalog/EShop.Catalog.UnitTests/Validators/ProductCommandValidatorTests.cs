using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Application.Products.Commands.DeleteProduct;
using EShop.Catalog.Application.Products.Commands.UpdateProduct;
using FluentValidation.TestHelper;

namespace EShop.Catalog.UnitTests.Validators;

[TestFixture]
public class ProductCommandValidatorTests
{
    private CreateProductCommandValidator _createValidator = null!;
    private UpdateProductCommandValidator _updateValidator = null!;
    private DeleteProductCommandValidator _deleteValidator = null!;

    [SetUp]
    public void SetUp()
    {
        _createValidator = new CreateProductCommandValidator();
        _updateValidator = new UpdateProductCommandValidator();
        _deleteValidator = new DeleteProductCommandValidator();
    }

    #region CreateProductCommandValidator

    [Test]
    public void CreateProduct_ValidCommand_ShouldHaveNoErrors()
    {
        // Arrange
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = Guid.NewGuid()
        };

        // Act
        var result = _createValidator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void CreateProduct_EmptyName_ShouldHaveError()
    {
        var command = new CreateProductCommand
        {
            Name = "",
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = Guid.NewGuid()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    public void CreateProduct_NameExceeds200Characters_ShouldHaveError()
    {
        var command = new CreateProductCommand
        {
            Name = new string('x', 201),
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = Guid.NewGuid()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    public void CreateProduct_EmptySku_ShouldHaveError()
    {
        var command = new CreateProductCommand
        {
            Name = "Test",
            Sku = "",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = Guid.NewGuid()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Test]
    public void CreateProduct_SkuWithSpecialChars_ShouldHaveError()
    {
        var command = new CreateProductCommand
        {
            Name = "Test",
            Sku = "SKU 001!",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = Guid.NewGuid()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Test]
    public void CreateProduct_SkuWithHyphensAndUnderscores_ShouldBeValid()
    {
        var command = new CreateProductCommand
        {
            Name = "Test",
            Sku = "SKU-001_A",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = Guid.NewGuid()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Sku);
    }

    [Test]
    public void CreateProduct_SkuExceeds50Characters_ShouldHaveError()
    {
        var command = new CreateProductCommand
        {
            Name = "Test",
            Sku = new string('A', 51),
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = Guid.NewGuid()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Test]
    public void CreateProduct_ZeroPrice_ShouldHaveError()
    {
        var command = new CreateProductCommand
        {
            Name = "Test",
            Sku = "SKU-001",
            Price = 0m,
            StockQuantity = 100,
            CategoryId = Guid.NewGuid()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Test]
    public void CreateProduct_NegativePrice_ShouldHaveError()
    {
        var command = new CreateProductCommand
        {
            Name = "Test",
            Sku = "SKU-001",
            Price = -10m,
            StockQuantity = 100,
            CategoryId = Guid.NewGuid()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Test]
    public void CreateProduct_NegativeStockQuantity_ShouldHaveError()
    {
        var command = new CreateProductCommand
        {
            Name = "Test",
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = -1,
            CategoryId = Guid.NewGuid()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.StockQuantity);
    }

    [Test]
    public void CreateProduct_EmptyCategoryId_ShouldHaveError()
    {
        var command = new CreateProductCommand
        {
            Name = "Test",
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = Guid.Empty
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.CategoryId);
    }

    #endregion

    #region UpdateProductCommandValidator

    [Test]
    public void UpdateProduct_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new UpdateProductCommand
        {
            ProductId = Guid.NewGuid(),
            Price = 39.99m,
            StockQuantity = 50
        };

        var result = _updateValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void UpdateProduct_EmptyProductId_ShouldHaveError()
    {
        var command = new UpdateProductCommand
        {
            ProductId = Guid.Empty,
            Price = 39.99m,
            StockQuantity = 50
        };

        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }

    [Test]
    public void UpdateProduct_ZeroPrice_ShouldHaveError()
    {
        var command = new UpdateProductCommand
        {
            ProductId = Guid.NewGuid(),
            Price = 0m,
            StockQuantity = 50
        };

        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Test]
    public void UpdateProduct_NegativeStockQuantity_ShouldHaveError()
    {
        var command = new UpdateProductCommand
        {
            ProductId = Guid.NewGuid(),
            Price = 39.99m,
            StockQuantity = -1
        };

        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.StockQuantity);
    }

    #endregion

    #region DeleteProductCommandValidator

    [Test]
    public void DeleteProduct_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new DeleteProductCommand { ProductId = Guid.NewGuid() };

        var result = _deleteValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void DeleteProduct_EmptyProductId_ShouldHaveError()
    {
        var command = new DeleteProductCommand { ProductId = Guid.Empty };

        var result = _deleteValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }

    #endregion
}
