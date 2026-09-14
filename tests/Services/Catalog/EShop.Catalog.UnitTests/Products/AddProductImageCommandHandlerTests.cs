using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Application.Products.Commands.AddProductImage;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class AddProductImageCommandHandlerTests
{
    private Mock<IProductRepository> _productRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private AddProductImageCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _handler = new AddProductImageCommandHandler(
            _productRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldAddImageAndReturnItsId()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, categoryId);

        var command = new AddProductImageCommand
        {
            ProductId = product.Id,
            Url = "https://example.com/img.jpg",
            AltText = "Alt text",
            DisplayOrder = 0
        };

        _productRepositoryMock
            .Setup(x => x.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(product.Images, Has.Count.EqualTo(1));
        Assert.That(result.Value, Is.EqualTo(product.Images.Single().Id));
        Assert.That(product.Images.Single().IsMain, Is.True, "the first image added becomes the main image");
        _productRepositoryMock.Verify(x => x.UpdateAsync(product, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentProduct_ShouldReturnNotFoundError()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var command = new AddProductImageCommand
        {
            ProductId = productId,
            Url = "https://example.com/img.jpg",
            DisplayOrder = 0
        };

        _productRepositoryMock
            .Setup(x => x.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Product.NotFound"));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void Handle_WithDuplicateUrl_ShouldLetDomainExceptionPropagate()
    {
        // Arrange — the handler deliberately does not convert this to a Result failure:
        // ProblemDetailsExceptionMiddleware maps DomainException to 400, which is correct here.
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, Guid.NewGuid());
        product.AddImage("https://example.com/img.jpg", "Alt text", 0);

        var command = new AddProductImageCommand
        {
            ProductId = product.Id,
            Url = "https://example.com/img.jpg",
            DisplayOrder = 1
        };

        _productRepositoryMock
            .Setup(x => x.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        // Act & Assert
        Assert.ThrowsAsync<DomainException>(() => _handler.Handle(command, CancellationToken.None));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A new main image changes MainImageUrl in every list, the per-category pages included —
    /// covered by the products:list family since Stage 6, not by an exact key from the handler.
    /// </summary>
    [Test]
    public void DeclaresItsOwnInvalidation()
    {
        var command = new AddProductImageCommand { ProductId = Guid.NewGuid() };

        Assert.That(command.CacheKeysToInvalidate, Is.EquivalentTo(ProductCacheKeys.AllDetailVariants(command.ProductId)));
        Assert.That(command.CacheFamiliesToInvalidate, Does.Contain(ProductCacheFamilies.ProductList));
    }
}
