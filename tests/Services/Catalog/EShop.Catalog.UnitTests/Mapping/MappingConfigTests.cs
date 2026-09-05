using System.Reflection;
using EShop.Catalog.Application.Mapping;
using EShop.Catalog.Application.Products.Queries.GetProductsById;
using EShop.Catalog.Domain.Entities;
using Mapster;
using MapsterMapper;

namespace EShop.Catalog.UnitTests.Mapping;

[TestFixture]
public class MappingConfigTests
{
    private IMapper _mapper = null!;
    private readonly Guid _validCategoryId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        var config = new TypeAdapterConfig();
        config.Scan(Assembly.GetAssembly(typeof(MappingConfig))!);
        _mapper = new Mapper(config);
    }

    [Test]
    public void ProductToProductDetailsDto_MainImageUrl_ShouldHonourIsMainOverDisplayOrder()
    {
        // Arrange — the lowest DisplayOrder image is not the main one; the mapping must
        // still pick the main image first, per the canonical ordering in the images plan.
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.AddImage("https://example.com/first.jpg", "First", 0);
        product.AddImage("https://example.com/second.jpg", "Second", 1);
        var secondImageId = product.Images.Single(i => i.Url.EndsWith("second.jpg")).Id;
        product.SetMainImage(secondImageId);

        // Act
        var dto = _mapper.Map<ProductDetailsDto>(product);

        // Assert
        Assert.That(dto.MainImageUrl, Is.EqualTo("https://example.com/second.jpg"));
    }

    [Test]
    public void ProductToProductDetailsDto_Images_ShouldBeOrderedByDisplayOrderThenCreatedAt()
    {
        // Arrange — added out of DisplayOrder order to prove the mapping sorts, not just
        // passes through insertion order.
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.AddImage("https://example.com/third.jpg", "Third", 2);
        product.AddImage("https://example.com/first.jpg", "First", 0);
        product.AddImage("https://example.com/second.jpg", "Second", 1);

        // Act
        var dto = _mapper.Map<ProductDetailsDto>(product);

        // Assert
        Assert.That(dto.Images.Select(i => i.Url), Is.EqualTo(new[]
        {
            "https://example.com/first.jpg",
            "https://example.com/second.jpg",
            "https://example.com/third.jpg"
        }));
    }

    [Test]
    public void ProductToProductDetailsDto_ShouldMapAttributes()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);
        product.AddAttribute("Color", "Red");
        product.AddAttribute("Size", "Large");

        // Act
        var dto = _mapper.Map<ProductDetailsDto>(product);

        // Assert
        Assert.That(dto.Attributes, Has.Count.EqualTo(2));
        Assert.That(dto.Attributes.Select(a => (a.Name, a.Value)), Is.EquivalentTo(new[]
        {
            ("Color", "Red"),
            ("Size", "Large")
        }));
    }

    [Test]
    public void ProductToProductDetailsDto_WithNoImages_MainImageUrlShouldBeNull()
    {
        // Arrange
        var product = Product.Create("Test", "SKU-001", 29.99m, 100, _validCategoryId);

        // Act
        var dto = _mapper.Map<ProductDetailsDto>(product);

        // Assert
        Assert.That(dto.MainImageUrl, Is.Null);
        Assert.That(dto.Images, Is.Empty);
    }
}
