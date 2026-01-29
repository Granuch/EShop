# ✅ Unit Testing Guide

Best practices для написання unit tests.

---

## What to Unit Test

**Domain Layer** (90% coverage):
- ✅ Entities (Product, Order, etc.)
- ✅ Value Objects (Money, Email, etc.)
- ✅ Domain Events
- ✅ Business Rules
- ✅ Aggregates

**Application Layer** (80% coverage):
- ✅ Command Handlers
- ✅ Query Handlers
- ✅ Validators (FluentValidation)
- ✅ Domain Event Handlers

**What NOT to Unit Test**:
- ❌ Framework code (EF Core, ASP.NET Core)
- ❌ Simple POCOs / DTOs
- ❌ Configurations
- ❌ Third-party libraries

---

## AAA Pattern

**Arrange** → **Act** → **Assert**

```csharp
[Test]
public void UpdatePrice_WithValidPrice_ShouldUpdateSuccessfully()
{
    // Arrange (Setup)
    var product = ProductBuilder.Create();
    var newPrice = new Money(199.99m);
    
    // Act (Execute)
    product.UpdatePrice(newPrice);
    
    // Assert (Verify)
    product.Price.Should().Be(newPrice);
    product.DomainEvents.Should().ContainSingle(e => e is ProductPriceChangedEvent);
}
```

---

## Test Naming Convention

**Format**: `MethodName_Scenario_ExpectedBehavior`

**Examples**:

```csharp
// ✅ Good Names
CreateProduct_WithNegativePrice_ShouldThrowDomainException()
Publish_WithZeroStock_ShouldThrowDomainException()
AddImage_WithValidUrl_ShouldAddImageToCollection()
UpdateStock_WithNegativeQuantity_ShouldThrowArgumentException()

// ❌ Bad Names
Test1()
ProductTest()
ShouldWork()
ValidateProduct()
```

---

## Domain Entity Tests

### Example: Product Entity

```csharp
[TestFixture]
public class ProductTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateProduct()
    {
        // Arrange
        var name = "Test Product";
        var description = "Test Description";
        var sku = "TEST-001";
        var price = new Money(99.99m);
        var stockQuantity = 10;
        var categoryId = Guid.NewGuid();
        var createdBy = "system";
        
        // Act
        var product = Product.Create(name, description, sku, price, stockQuantity, categoryId, createdBy);
        
        // Assert
        product.Should().NotBeNull();
        product.Name.Should().Be(name);
        product.Description.Should().Be(description);
        product.Sku.Should().Be(sku);
        product.Price.Should().Be(price);
        product.StockQuantity.Should().Be(stockQuantity);
        product.CategoryId.Should().Be(categoryId);
        product.Status.Should().Be(ProductStatus.Draft);
        product.CreatedBy.Should().Be(createdBy);
        
        // Domain event raised
        product.DomainEvents.Should().ContainSingle();
        product.DomainEvents.First().Should().BeOfType<ProductCreatedEvent>();
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("   ")]
    public void Create_WithInvalidName_ShouldThrowDomainException(string invalidName)
    {
        // Arrange
        var price = new Money(99.99m);
        
        // Act
        var act = () => Product.Create(
            invalidName, 
            "Description", 
            "SKU-001", 
            price, 
            10, 
            Guid.NewGuid(), 
            "system");
        
        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Product name is required");
    }

    [Test]
    public void UpdatePrice_WithNegativePrice_ShouldThrowDomainException()
    {
        // Arrange
        var product = ProductBuilder.Create();
        var invalidPrice = new Money(-10);
        
        // Act
        var act = () => product.UpdatePrice(invalidPrice);
        
        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Price must be greater than zero");
    }

    [Test]
    public void Publish_WithZeroStock_ShouldThrowDomainException()
    {
        // Arrange
        var product = ProductBuilder.Create();
        product.UpdateStock(0);
        
        // Act
        var act = () => product.Publish();
        
        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot publish product with zero stock");
    }

    [Test]
    public void UpdateStock_WithValidQuantity_ShouldUpdateStock()
    {
        // Arrange
        var product = ProductBuilder.Create();
        var newQuantity = 50;
        
        // Act
        product.UpdateStock(newQuantity);
        
        // Assert
        product.StockQuantity.Should().Be(newQuantity);
    }

    [Test]
    public void AddImage_WithValidData_ShouldAddImageToCollection()
    {
        // Arrange
        var product = ProductBuilder.Create();
        var imageUrl = "https://example.com/image.jpg";
        var altText = "Product Image";
        
        // Act
        product.AddImage(imageUrl, altText, displayOrder: 1);
        
        // Assert
        product.Images.Should().ContainSingle();
        product.Images.First().Url.Should().Be(imageUrl);
        product.Images.First().AltText.Should().Be(altText);
    }
}
```

---

## Value Object Tests

### Example: Money Value Object

```csharp
[TestFixture]
public class MoneyTests
{
    [Test]
    public void Constructor_WithValidAmount_ShouldCreateMoney()
    {
        // Arrange & Act
        var money = new Money(99.99m);
        
        // Assert
        money.Amount.Should().Be(99.99m);
        money.Currency.Should().Be("USD");
    }

    [Test]
    public void Constructor_WithNegativeAmount_ShouldThrowArgumentException()
    {
        // Act
        var act = () => new Money(-10);
        
        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Amount cannot be negative");
    }

    [Test]
    public void Add_WithSameCurrency_ShouldReturnNewMoney()
    {
        // Arrange
        var money1 = new Money(100);
        var money2 = new Money(50);
        
        // Act
        var result = money1.Add(money2);
        
        // Assert
        result.Amount.Should().Be(150);
        result.Currency.Should().Be("USD");
    }

    [Test]
    public void Add_WithDifferentCurrency_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(50, "EUR");
        
        // Act
        var act = () => money1.Add(money2);
        
        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot add different currencies");
    }

    [Test]
    public void Equals_WithSameValues_ShouldReturnTrue()
    {
        // Arrange
        var money1 = new Money(100);
        var money2 = new Money(100);
        
        // Act & Assert
        money1.Should().Be(money2); // Value equality
        (money1 == money2).Should().BeTrue();
    }

    [Test]
    public void Equals_WithDifferentValues_ShouldReturnFalse()
    {
        // Arrange
        var money1 = new Money(100);
        var money2 = new Money(200);
        
        // Act & Assert
        money1.Should().NotBe(money2);
    }
}
```

---

## Command Handler Tests

### Example: CreateProductCommandHandler

```csharp
[TestFixture]
public class CreateProductCommandHandlerTests
{
    private Mock<IProductRepository> _repositoryMock = null!;
    private Mock<ICategoryRepository> _categoryRepositoryMock = null!;
    private CreateProductCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IProductRepository>();
        _categoryRepositoryMock = new Mock<ICategoryRepository>();
        _handler = new CreateProductCommandHandler(
            _repositoryMock.Object,
            _categoryRepositoryMock.Object,
            Mock.Of<ILogger<CreateProductCommandHandler>>());
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldCreateProduct()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Sku = "TEST-001",
            Price = 99.99m,
            StockQuantity = 10,
            CategoryId = categoryId
        };

        _categoryRepositoryMock
            .Setup(x => x.GetByIdAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CategoryBuilder.Create(categoryId));

        _repositoryMock
            .Setup(x => x.GetBySkuAsync(command.Sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null); // SKU is unique

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty(); // ProductId returned

        _repositoryMock.Verify(
            x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Once);
        
        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_WithDuplicateSku_ShouldReturnFailure()
    {
        // Arrange
        var command = new CreateProductCommand { Sku = "DUPLICATE-SKU" };
        
        var existingProduct = ProductBuilder.Create();
        _repositoryMock
            .Setup(x => x.GetBySkuAsync(command.Sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProduct);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("SKU_EXISTS");
        result.Error.Message.Should().Contain("already exists");

        _repositoryMock.Verify(
            x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task Handle_WithNonExistentCategory_ShouldReturnFailure()
    {
        // Arrange
        var command = new CreateProductCommand { CategoryId = Guid.NewGuid() };

        _categoryRepositoryMock
            .Setup(x => x.GetByIdAsync(command.CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
    }
}
```

---

## Validator Tests

### Example: CreateProductCommandValidator

```csharp
[TestFixture]
public class CreateProductCommandValidatorTests
{
    private CreateProductCommandValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new CreateProductCommandValidator();
    }

    [Test]
    public void Validate_WithValidCommand_ShouldPassValidation()
    {
        // Arrange
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Description",
            Sku = "TEST-001",
            Price = 99.99m,
            StockQuantity = 10,
            CategoryId = Guid.NewGuid()
        };

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("   ")]
    public void Validate_WithInvalidName_ShouldFailValidation(string invalidName)
    {
        // Arrange
        var command = new CreateProductCommand { Name = invalidName };

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [TestCase(-1)]
    [TestCase(0)]
    public void Validate_WithInvalidPrice_ShouldFailValidation(decimal invalidPrice)
    {
        // Arrange
        var command = new CreateProductCommand { Price = invalidPrice };

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Price");
    }

    [Test]
    public void Validate_WithLongName_ShouldFailValidation()
    {
        // Arrange
        var command = new CreateProductCommand 
        { 
            Name = new string('a', 201) // Max is 200
        };

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => 
            e.PropertyName == "Name" && 
            e.ErrorMessage.Contains("200"));
    }
}
```

---

## Test Builders (Bogus)

```csharp
public class ProductBuilder
{
    private static readonly Faker<Product> Faker = new Faker<Product>()
        .CustomInstantiator(f =>
        {
            var product = Product.Create(
                name: f.Commerce.ProductName(),
                description: f.Lorem.Paragraph(),
                sku: f.Random.AlphaNumeric(10).ToUpper(),
                price: new Money(f.Random.Decimal(10, 1000)),
                stockQuantity: f.Random.Int(0, 100),
                categoryId: Guid.NewGuid(),
                createdBy: "system"
            );
            
            // Clear domain events (for clean tests)
            product.ClearDomainEvents();
            
            return product;
        });

    public static Product Create() => Faker.Generate();

    public static List<Product> CreateMany(int count) => Faker.Generate(count);

    public static Product CreateWithPrice(decimal price)
    {
        var product = Create();
        product.UpdatePrice(new Money(price));
        return product;
    }

    public static Product CreateOutOfStock()
    {
        var product = Create();
        product.UpdateStock(0);
        return product;
    }

    public static Product CreatePublished()
    {
        var product = Create();
        product.UpdateStock(10); // Ensure stock > 0
        product.Publish();
        return product;
    }
}

// Usage
var product = ProductBuilder.Create();
var expensiveProduct = ProductBuilder.CreateWithPrice(999.99m);
var outOfStockProduct = ProductBuilder.CreateOutOfStock();
```

---

## Test Data Constants

```csharp
public static class TestData
{
    public static class Products
    {
        public const string ValidName = "Test Product";
        public const string ValidDescription = "Test Description";
        public const string ValidSku = "TEST-001";
        public const decimal ValidPrice = 99.99m;
        public const int ValidStock = 10;
    }

    public static class Categories
    {
        public static readonly Guid ElectronicsId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid ClothingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    }
}

// Usage
var product = Product.Create(
    TestData.Products.ValidName,
    TestData.Products.ValidDescription,
    TestData.Products.ValidSku,
    new Money(TestData.Products.ValidPrice),
    TestData.Products.ValidStock,
    TestData.Categories.ElectronicsId,
    "system"
);
```

---

## Common Pitfalls

### ❌ Don't: Multiple Assertions in One Test

```csharp
// ❌ Bad: Testing multiple scenarios in one test
[Fact]
public void ProductTests()
{
    var product = ProductBuilder.Create();
    product.Name.Should().NotBeNullOrEmpty();
    
    product.UpdatePrice(new Money(-10)); // Should throw
    // But test continues...
}
```

### ✅ Do: One Logical Assertion per Test

```csharp
// ✅ Good: Separate tests
[Fact]
public void Create_ShouldSetName()
{
    var product = ProductBuilder.Create();
    product.Name.Should().NotBeNullOrEmpty();
}

[Fact]
public void UpdatePrice_WithNegativePrice_ShouldThrow()
{
    var product = ProductBuilder.Create();
    var act = () => product.UpdatePrice(new Money(-10));
    act.Should().Throw<DomainException>();
}
```

---

### ❌ Don't: Test Implementation Details

```csharp
// ❌ Bad: Testing private method
[Fact]
public void ValidateName_WithInvalidName_ShouldThrow()
{
    // Testing private method via reflection - brittle!
}
```

### ✅ Do: Test Public Behavior

```csharp
// ✅ Good: Test public method that calls ValidateName internally
[Fact]
public void Create_WithInvalidName_ShouldThrow()
{
    var act = () => Product.Create("", ...);
    act.Should().Throw<DomainException>();
}
```

---

## Running Tests

```bash
# Run all unit tests
dotnet test tests/**/*UnitTests.csproj

# Run specific test
dotnet test --filter "FullyQualifiedName~ProductTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run in watch mode (auto-run on file change)
dotnet watch test tests/Catalog.UnitTests/
```

---

## Summary

✅ **Do**:
- Test domain logic extensively
- Use AAA pattern
- Keep tests fast (< 100ms)
- Use descriptive names
- Mock external dependencies
- Use test builders for complex objects

❌ **Don't**:
- Test framework code
- Test implementation details
- Have slow tests
- Share state between tests
- Ignore failing tests

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
