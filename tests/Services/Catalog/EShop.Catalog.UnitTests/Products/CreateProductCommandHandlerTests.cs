using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class CreateProductCommandHandlerTests
{
    private Mock<IProductRepository> _productRepositoryMock = null!;
    private Mock<ICategoryRepository> _categoryRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private CreateProductCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _categoryRepositoryMock = new Mock<ICategoryRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _handler = new CreateProductCommandHandler(
            _productRepositoryMock.Object,
            _categoryRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldReturnSuccessWithProductId()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = categoryId
        };

        _productRepositoryMock
            .Setup(x => x.GetBySkuAsync(command.Sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        _categoryRepositoryMock
            .Setup(x => x.GetById(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Category.Create("Test Category", null, null));

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.EqualTo(Guid.Empty));
        _productRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithInlineImagesAndAttributes_ShouldAttachThemToProduct()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = categoryId,
            Images =
            [
                new CreateProductImageRequest { Url = "https://example.com/first.jpg", AltText = "First", DisplayOrder = 0 },
                new CreateProductImageRequest { Url = "https://example.com/second.jpg", AltText = "Second", DisplayOrder = 1 }
            ],
            Attributes =
            [
                new CreateProductAttributeRequest { Name = "Color", Value = "Red" },
                new CreateProductAttributeRequest { Name = "Size", Value = "Large" }
            ]
        };

        Product? addedProduct = null;

        _productRepositoryMock
            .Setup(x => x.GetBySkuAsync(command.Sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        _categoryRepositoryMock
            .Setup(x => x.GetById(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Category.Create("Test Category", null, null));

        _productRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Callback<Product, CancellationToken>((p, _) => addedProduct = p);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(addedProduct, Is.Not.Null);
        Assert.That(addedProduct!.Images, Has.Count.EqualTo(2));
        Assert.That(addedProduct.Attributes, Has.Count.EqualTo(2));
        Assert.That(addedProduct.Images.Count(i => i.IsMain), Is.EqualTo(1), "the first image added becomes the main image");
        Assert.That(addedProduct.Images.Single(i => i.IsMain).Url, Is.EqualTo("https://example.com/first.jpg"));
        Assert.That(addedProduct.Attributes.Select(a => a.Name), Is.EquivalentTo(new[] { "Color", "Size" }));
    }

    [Test]
    public void Handle_WithInvalidInlineImage_ShouldThrowBeforePersisting()
    {
        // Arrange — a rejected image must leave nothing behind: AddImage runs before AddAsync,
        // so no partial product is ever handed to the repository.
        var categoryId = Guid.NewGuid();
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = categoryId,
            Images =
            [
                new CreateProductImageRequest { Url = "not-an-absolute-url", DisplayOrder = 0 }
            ]
        };

        _productRepositoryMock
            .Setup(x => x.GetBySkuAsync(command.Sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        _categoryRepositoryMock
            .Setup(x => x.GetById(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Category.Create("Test Category", null, null));

        // Act & Assert
        Assert.ThrowsAsync<DomainException>(() => _handler.Handle(command, CancellationToken.None));
        _productRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithDuplicateSku_ShouldReturnSkuConflictError()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = categoryId
        };

        _productRepositoryMock
            .Setup(x => x.GetBySkuAsync(command.Sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Product.Create("Existing", "SKU-001", 19.99m, 50, categoryId));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Product.SkuConflict"));
        _productRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithNonExistentCategory_ShouldReturnCategoryNotFoundError()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Sku = "SKU-001",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = categoryId
        };

        _productRepositoryMock
            .Setup(x => x.GetBySkuAsync(command.Sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        _categoryRepositoryMock
            .Setup(x => x.GetById(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Category.NotFound"));
        _productRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
