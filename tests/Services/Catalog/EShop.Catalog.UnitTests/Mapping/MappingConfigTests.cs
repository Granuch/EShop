using System.Reflection;
using EShop.Catalog.Application.Mapping;
using EShop.Catalog.Application.Products.Queries.GetProducts;
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

    /// <summary>
    /// DEBT-15. The list DTO must pick the same image as the detail DTO.
    ///
    /// <para>
    /// This config ordered by <c>DisplayOrder</c> alone and ignored <c>IsMain</c>, so a product
    /// whose main image was not also first in display order showed one image in a list and a
    /// different one on its detail page. It was latent rather than live — the list endpoints
    /// project through <c>ProductQueryService</c>'s hand-written select and never reach Mapster —
    /// which is precisely what made it dangerous: switching the list path to Mapster would have
    /// reintroduced the inconsistency with nothing failing.
    /// </para>
    /// </summary>
    [Test]
    public void ProductToProductDto_MainImageUrl_ShouldAgreeWithProductDetailsDto()
    {
        var product = Product.Create("Test", "SKU-002", 29.99m, 100, _validCategoryId);
        product.AddImage("https://example.com/first.jpg", "First", 0);
        product.AddImage("https://example.com/second.jpg", "Second", 1);
        var secondImageId = product.Images.Single(i => i.Url.EndsWith("second.jpg")).Id;
        product.SetMainImage(secondImageId);

        var listDto = _mapper.Map<ProductDto>(product);
        var detailsDto = _mapper.Map<ProductDetailsDto>(product);

        Assert.That(listDto.MainImageUrl, Is.EqualTo("https://example.com/second.jpg"),
            "IsMain wins over DisplayOrder in the list projection too");
        Assert.That(listDto.MainImageUrl, Is.EqualTo(detailsDto.MainImageUrl),
            "a product must not show one image in a list and another on its detail page");
    }

    /// <summary>
    /// The main image can be implicit: <c>Product.AddImage</c> flags the first image added as main
    /// (<c>if (_images.Count == 0)</c>), so a product with images always has one and "no main
    /// image" is unreachable through the domain API. That matters here because it means the
    /// <c>IsMain</c> term is load-bearing even for products nobody ever called
    /// <c>SetMainImage</c> on — an image added later with a lower DisplayOrder must not displace it.
    /// </summary>
    [Test]
    public void ProductToProductDto_MainImageIsImplicitlyTheFirstAdded_AndBothDtosAgree()
    {
        var product = Product.Create("Test", "SKU-003", 29.99m, 100, _validCategoryId);
        product.AddImage("https://example.com/second.jpg", "Second", 1);
        product.AddImage("https://example.com/first.jpg", "First", 0);

        var listDto = _mapper.Map<ProductDto>(product);
        var detailsDto = _mapper.Map<ProductDetailsDto>(product);

        Assert.That(listDto.MainImageUrl, Is.EqualTo("https://example.com/second.jpg"),
            "the first image added is implicitly main, so it wins despite the higher DisplayOrder");
        Assert.That(listDto.MainImageUrl, Is.EqualTo(detailsDto.MainImageUrl));
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
