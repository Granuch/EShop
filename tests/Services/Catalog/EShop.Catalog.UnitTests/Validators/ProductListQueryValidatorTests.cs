using EShop.Catalog.Application.Products.Queries.GetNewestProducts;
using EShop.Catalog.Application.Products.Queries.GetProductByCategory;
using EShop.Catalog.Application.Products.Queries.GetProducts;

namespace EShop.Catalog.UnitTests.Validators;

[TestFixture]
public class ProductListQueryValidatorTests
{
    #region GetProductsQuery

    [Test]
    public void GetProducts_RejectsACursor_InsteadOfIgnoringIt()
    {
        // H4. Ignoring it served offset page 1 with a 200 — a client paging by cursor re-read the
        // first page forever and could not tell.
        var result = new GetProductsQueryValidator().Validate(new GetProductsQuery { Cursor = "anything" });

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Select(e => e.ErrorMessage), Has.Some.Contains("/api/v1/products/newest"));
    }

    [Test]
    public void GetProducts_WithNoCursor_IsValid()
    {
        Assert.That(new GetProductsQueryValidator().Validate(new GetProductsQuery()).IsValid, Is.True);
    }

    #endregion

    #region GetNewestProductsQuery

    [Test]
    public void GetNewest_AcceptsACursorItIssued()
    {
        var cursor = new ProductCursor(DateTime.UtcNow, Guid.NewGuid()).Encode();

        Assert.That(new GetNewestProductsQueryValidator().Validate(new GetNewestProductsQuery { Cursor = cursor }).IsValid, Is.True);
    }

    [Test]
    public void GetNewest_RejectsAnUnreadableCursor()
    {
        var result = new GetNewestProductsQueryValidator().Validate(new GetNewestProductsQuery { Cursor = "garbage" });

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Select(e => e.PropertyName), Has.Member(nameof(GetNewestProductsQuery.Cursor)));
    }

    [TestCase(0)]
    [TestCase(101)]
    public void GetNewest_RejectsAPageSizeOutOfRange(int pageSize)
    {
        var result = new GetNewestProductsQueryValidator().Validate(new GetNewestProductsQuery { PageSize = pageSize });

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Select(e => e.PropertyName), Has.Member(nameof(GetNewestProductsQuery.PageSize)),
            "the error must name the parameter the client sent");
    }

    [Test]
    public void GetNewest_WithNothingSet_IsValid()
    {
        Assert.That(new GetNewestProductsQueryValidator().Validate(new GetNewestProductsQuery()).IsValid, Is.True);
    }

    #endregion

    #region GetProductByCategoryQuery

    [TestCase(0)]
    [TestCase(101)]
    public void GetByCategory_RejectsAPageSizeOutOfRange(int pageSize)
    {
        // The upper bound is what stops retiring the 200 cap from becoming an unbounded read.
        var result = new GetProductByCategoryQueryValidator()
            .Validate(new GetProductByCategoryQuery { CategoryId = Guid.NewGuid(), PageSize = pageSize });

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void GetByCategory_RejectsPageZero()
    {
        var result = new GetProductByCategoryQueryValidator()
            .Validate(new GetProductByCategoryQuery { CategoryId = Guid.NewGuid(), PageNumber = 0 });

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void GetByCategory_WithNoPaging_IsValid()
    {
        var result = new GetProductByCategoryQueryValidator()
            .Validate(new GetProductByCategoryQuery { CategoryId = Guid.NewGuid() });

        Assert.That(result.IsValid, Is.True);
    }

    #endregion
}
