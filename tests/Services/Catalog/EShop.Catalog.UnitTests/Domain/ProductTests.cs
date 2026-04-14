using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Events;

namespace EShop.Catalog.UnitTests.Domain;

[TestFixture]
public class ProductTests
{
    private readonly Guid _validCategoryId = Guid.NewGuid();

    #region Create

    [Test]
    public void Create_WithValidParameters_ShouldCreateProduct()
    {
        // Act
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, _validCategoryId);

        // Assert
        Assert.That(product, Is.Not.Null);
        Assert.That(product.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(product.Name, Is.EqualTo("Test Product"));
        Assert.That(product.Sku, Is.EqualTo("SKU-001"));
        Assert.That(product.Price, Is.EqualTo(29.99m));
        Assert.That(product.StockQuantity, Is.EqualTo(100));
        Assert.That(product.CategoryId, Is.EqualTo(_validCategoryId));
        Assert.That(product.Status, Is.EqualTo(ProductStatus.Draft));
        Assert.That(product.IsDeleted, Is.False);
        Assert.That(product.DeletedAt, Is.Null);
    }

    [Test]
    public void Create_WithValidParameters_ShouldRaiseProductCreatedEvent()
    {
        // Act
        var product = Product.Create("Test Product", "SKU-001", 29.99m, 100, _validCategoryId);

        // Assert
        Assert.That(product.DomainEvents, Has.Count.EqualTo(1));
        var domainEvent = product.DomainEvents[0] as ProductCreatedEvent;
        Assert.That(domainEvent, Is.Not.Null);
        Assert.That(domainEvent!.ProductId, Is.EqualTo(product.Id));
        Assert.That(domainEvent.ProductName, Is.EqualTo("Test Product"));
        Assert.That(domainEvent.Price, Is.EqualTo(29.99m));
    }

    [Test]
    public void Create_WithEmptyName_ShouldThrowDomainException()
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create("", "SKU-001", 29.99m, 100, _validCategoryId));
        Assert.That(ex!.Message, Does.Contain("name"));
    }

    [Test]
    public void Create_WithWhitespaceName_ShouldThrowDomainException()
    {
        // Act & Assert
        Assert.Throws<DomainException>(() =>
            Product.Create("   ", "SKU-001", 29.99m, 100, _validCategoryId));
    }

    [Test]
    public void Create_WithNullName_ShouldThrowDomainException()
    {
        // Act & Assert
        Assert.Throws<DomainException>(() =>
            Product.Create(null!, "SKU-001", 29.99m, 100, _validCategoryId));
    }

    [Test]
    public void Create_WithEmptySku_ShouldThrowDomainException()
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create("Test", "", 29.99m, 100, _validCategoryId));
        Assert.That(ex!.Message, Does.Contain("SKU"));
    }

    [Test]
    public void Create_WithZeroPrice_ShouldThrowDomainException()
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create("Test", "SKU-001", 0m, 100, _validCategoryId));
        Assert.That(ex!.Message, Does.Contain("Price"));
    }

    [Test]
    public void Create_WithNegativePrice_ShouldThrowDomainException()
    {
        // Act & Assert
        Assert.Throws<DomainException>(() =>
            Product.Create("Test", "SKU-001", -10m, 100, _validCategoryId));
    }

    [Test]
    public void Create_WithNegativeStockQuantity_ShouldThrowDomainException()
    {
        // Act & Assert
        Assert.Throws<DomainException>(() =>
            Product.Create("Test", "SKU-001", 29.99m, -1, _validCategoryId));
    }

    [Test]
    public void Create_WithZeroStockQuantity_ShouldSucceed()
    {
        // Act
        var product = Product.Create("Test", "SKU-001", 29.99m, 0, _validCategoryId);

        // Assert
        Assert.That(product.StockQuantity, Is.EqualTo(0));
    }

    #endregion

    #region UpdatePrice

    [Test]
    public void UpdatePrice_WithValidPrice_ShouldUpdatePriceAndRaiseEvent()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.ClearDomainEvents();

        // Act
        product.UpdatePrice(39.99m);

        // Assert
        Assert.That(product.Price, Is.EqualTo(39.99m));
        Assert.That(product.DomainEvents, Has.Count.EqualTo(1));
        var domainEvent = product.DomainEvents[0] as ProductPriceChangedEvent;
        Assert.That(domainEvent, Is.Not.Null);
        Assert.That(domainEvent!.OldPrice, Is.EqualTo(29.99m));
        Assert.That(domainEvent.NewPrice, Is.EqualTo(39.99m));
        Assert.That(domainEvent.ProductId, Is.EqualTo(product.Id));
    }

    [Test]
    public void UpdatePrice_WithSamePrice_ShouldNotRaiseEvent()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.ClearDomainEvents();

        // Act
        product.UpdatePrice(29.99m);

        // Assert
        Assert.That(product.Price, Is.EqualTo(29.99m));
        Assert.That(product.DomainEvents, Has.Count.EqualTo(0));
    }

    [Test]
    public void UpdatePrice_WithZeroPrice_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act & Assert
        Assert.Throws<DomainException>(() => product.UpdatePrice(0m));
    }

    [Test]
    public void UpdatePrice_WithNegativePrice_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act & Assert
        Assert.Throws<DomainException>(() => product.UpdatePrice(-5m));
    }

    #endregion

    #region UpdateStock

    [Test]
    public void UpdateStock_WithValidQuantity_ShouldUpdateStock()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.ClearDomainEvents();

        // Act
        product.UpdateStock(50);

        // Assert
        Assert.That(product.StockQuantity, Is.EqualTo(50));
        Assert.That(product.DomainEvents, Has.Count.EqualTo(0));
    }

    [Test]
    public void UpdateStock_ToZero_ShouldRaiseOutOfStockEvent()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.ClearDomainEvents();

        // Act
        product.UpdateStock(0);

        // Assert
        Assert.That(product.StockQuantity, Is.EqualTo(0));
        Assert.That(product.DomainEvents, Has.Count.EqualTo(1));
        Assert.That(product.DomainEvents[0], Is.TypeOf<ProductOutOfStockEvent>());
        var evt = (ProductOutOfStockEvent)product.DomainEvents[0];
        Assert.That(evt.ProductId, Is.EqualTo(product.Id));
    }

    [Test]
    public void UpdateStock_FromZeroToPositive_ShouldRaiseBackInStockEvent()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 0, _validCategoryId);
        product.ClearDomainEvents();

        // Act
        product.UpdateStock(10);

        // Assert
        Assert.That(product.StockQuantity, Is.EqualTo(10));
        Assert.That(product.DomainEvents, Has.Count.EqualTo(1));
        Assert.That(product.DomainEvents[0], Is.TypeOf<ProductBackInStockEvent>());
    }

    [Test]
    public void UpdateStock_WithSameQuantity_ShouldNotRaiseEvent()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 50, _validCategoryId);
        product.ClearDomainEvents();

        // Act
        product.UpdateStock(50);

        // Assert
        Assert.That(product.DomainEvents, Has.Count.EqualTo(0));
    }

    [Test]
    public void UpdateStock_WithNegativeQuantity_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act & Assert
        Assert.Throws<DomainException>(() => product.UpdateStock(-1));
    }

    [Test]
    public void UpdateStock_OnDeletedProduct_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.SoftDelete();

        // Act & Assert
        Assert.Throws<DomainException>(() => product.UpdateStock(50));
    }

    #endregion

    #region Publish

    [Test]
    public void Publish_DraftProduct_ShouldSetStatusToActive()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act
        product.Publish();

        // Assert
        Assert.That(product.Status, Is.EqualTo(ProductStatus.Active));
    }

    [Test]
    public void Publish_DeletedProduct_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.SoftDelete();

        // Act & Assert
        Assert.Throws<DomainException>(() => product.Publish());
    }

    [Test]
    public void Publish_AlreadyActiveProduct_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.Publish();

        // Act & Assert
        Assert.Throws<DomainException>(() => product.Publish());
    }

    #endregion

    #region SoftDelete

    [Test]
    public void SoftDelete_ActiveProduct_ShouldMarkAsDeleted()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act
        product.SoftDelete();

        // Assert
        Assert.That(product.IsDeleted, Is.True);
        Assert.That(product.DeletedAt, Is.Not.Null);
        Assert.That(product.Status, Is.EqualTo(ProductStatus.Discontinued));
    }

    [Test]
    public void SoftDelete_AlreadyDeletedProduct_ShouldBeIdempotent()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.SoftDelete();
        var firstDeletedAt = product.DeletedAt;

        // Act — second call should not throw
        product.SoftDelete();

        // Assert
        Assert.That(product.IsDeleted, Is.True);
        Assert.That(product.DeletedAt, Is.EqualTo(firstDeletedAt));
    }

    #endregion

    #region Images

    [Test]
    public void AddImage_WithValidParameters_ShouldAddImage()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act
        product.AddImage("https://example.com/img.jpg", "Alt text", 0);

        // Assert
        Assert.That(product.Images, Has.Count.EqualTo(1));
        Assert.That(product.Images.First().Url, Is.EqualTo("https://example.com/img.jpg"));
        Assert.That(product.Images.First().AltText, Is.EqualTo("Alt text"));
        Assert.That(product.Images.First().IsMain, Is.True);
    }

    [Test]
    public void SetMainImage_WithMultipleImages_ShouldSwitchMainImage()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.AddImage("https://example.com/img-1.jpg", "Main", 0);
        product.AddImage("https://example.com/img-2.png", "Secondary", 1);
        var secondaryImageId = product.Images.Single(i => i.Url.EndsWith("img-2.png")).Id;

        // Act
        product.SetMainImage(secondaryImageId);

        // Assert
        Assert.That(product.Images.Count(i => i.IsMain), Is.EqualTo(1));
        Assert.That(product.Images.Single(i => i.Id == secondaryImageId).IsMain, Is.True);
    }

    [Test]
    public void RemoveImage_WhenRemovingMainImage_ShouldPromoteNextImageAsMain()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.AddImage("https://example.com/img-main.jpg", "Main", 0);
        product.AddImage("https://example.com/img-second.png", "Second", 1);
        var mainImageId = product.Images.Single(i => i.IsMain).Id;

        // Act
        product.RemoveImage(mainImageId);

        // Assert
        Assert.That(product.Images, Has.Count.EqualTo(1));
        Assert.That(product.Images.Single().IsMain, Is.True);
    }

    [Test]
    public void AddImage_DuplicateUrl_ShouldNotAddDuplicate()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.AddImage("https://example.com/img.jpg", "Alt text", 0);

        // Act
        product.AddImage("https://example.com/img.jpg", "Alt text 2", 1);

        // Assert
        Assert.That(product.Images, Has.Count.EqualTo(1));
    }

    [Test]
    public void AddImage_WithUnsupportedFormat_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act & Assert
        Assert.Throws<DomainException>(() =>
            product.AddImage("https://example.com/manual.pdf", "Alt text", 0));
    }

    [Test]
    public void AddImage_EmptyUrl_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act & Assert
        Assert.Throws<DomainException>(() => product.AddImage("", "Alt text", 0));
    }

    [Test]
    public void AddImage_NegativeDisplayOrder_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act & Assert
        Assert.Throws<DomainException>(() =>
            product.AddImage("https://example.com/img.jpg", "Alt text", -1));
    }

    [Test]
    public void AddImage_OnDeletedProduct_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.SoftDelete();

        // Act & Assert
        Assert.Throws<DomainException>(() =>
            product.AddImage("https://example.com/img.jpg", "Alt text", 0));
    }

    [Test]
    public void RemoveImage_ExistingImage_ShouldRemoveImage()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.AddImage("https://example.com/img.jpg", "Alt text", 0);
        var imageId = product.Images.First().Id;

        // Act
        product.RemoveImage(imageId);

        // Assert
        Assert.That(product.Images, Has.Count.EqualTo(0));
    }

    [Test]
    public void RemoveImage_NonExistentImage_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act & Assert
        Assert.Throws<DomainException>(() => product.RemoveImage(Guid.NewGuid()));
    }

    [Test]
    public void RemoveImage_OnDeletedProduct_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.AddImage("https://example.com/img.jpg", "Alt text", 0);
        var imageId = product.Images.First().Id;
        product.SoftDelete();

        // Act & Assert
        Assert.Throws<DomainException>(() => product.RemoveImage(imageId));
    }

    #endregion

    #region Attributes

    [Test]
    public void AddAttribute_WithValidParameters_ShouldAddAttribute()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act
        product.AddAttribute("Color", "Red");

        // Assert
        Assert.That(product.Attributes, Has.Count.EqualTo(1));
        Assert.That(product.Attributes.First().Name, Is.EqualTo("Color"));
        Assert.That(product.Attributes.First().Value, Is.EqualTo("Red"));
    }

    [Test]
    public void AddAttribute_WithExtraWhitespace_ShouldTrimNameAndValue()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act
        product.AddAttribute("  Color  ", "  Red  ");

        // Assert
        Assert.That(product.Attributes.Single().Name, Is.EqualTo("Color"));
        Assert.That(product.Attributes.Single().Value, Is.EqualTo("Red"));
    }

    [Test]
    public void AddAttribute_WithLongName_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        var longName = new string('A', 101);

        // Act & Assert
        Assert.Throws<DomainException>(() => product.AddAttribute(longName, "Red"));
    }

    [Test]
    public void AddAttribute_EmptyName_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act & Assert
        Assert.Throws<DomainException>(() => product.AddAttribute("", "Red"));
    }

    [Test]
    public void AddAttribute_EmptyValue_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act & Assert
        Assert.Throws<DomainException>(() => product.AddAttribute("Color", ""));
    }

    [Test]
    public void AddAttribute_OnDeletedProduct_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.SoftDelete();

        // Act & Assert
        Assert.Throws<DomainException>(() => product.AddAttribute("Color", "Red"));
    }

    #endregion

    #region DomainEvents

    [Test]
    public void ClearDomainEvents_ShouldRemoveAllEvents()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        Assert.That(product.DomainEvents, Has.Count.EqualTo(1));

        // Act
        product.ClearDomainEvents();

        // Assert
        Assert.That(product.DomainEvents, Has.Count.EqualTo(0));
    }

    [Test]
    public void Version_NewProduct_ShouldBeZero()
    {
        // Act
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Assert
        Assert.That(product.Version, Is.EqualTo(0));
    }

    [Test]
    public void IncrementVersion_ShouldIncreaseByOne()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        var initialVersion = product.Version;

        // Act
        product.IncrementVersion();

        // Assert
        Assert.That(product.Version, Is.EqualTo(initialVersion + 1));
    }

    #endregion
}
