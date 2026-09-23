using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Application.Products.Commands.ImportProducts;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

/// <summary>
/// Admin panel S16. The import handler's refusals, each of which must be decided before anything is added — the property
/// that makes a partial import commit only its good rows.
/// </summary>
[TestFixture]
public class ImportProductsCommandTests
{
    private readonly Guid _categoryId = Guid.NewGuid();
    private Mock<IProductRepository> _products = null!;
    private Mock<ICategoryRepository> _categories = null!;
    private Mock<IUnitOfWork> _unitOfWork = null!;
    private List<Product> _added = null!;
    private HashSet<string> _takenSkus = null!;

    [SetUp]
    public void SetUp()
    {
        _added = [];
        _takenSkus = new HashSet<string>(StringComparer.Ordinal);

        _products = new Mock<IProductRepository>();
        _products.Setup(r => r.GetTakenSkusAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<string> skus, CancellationToken _) => skus.Where(_takenSkus.Contains).ToHashSet(StringComparer.Ordinal));
        _products.Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Callback((Product p, CancellationToken _) => _added.Add(p))
            .Returns(Task.CompletedTask);

        _categories = new Mock<ICategoryRepository>();
        _categories.Setup(r => r.GetExistingIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Where(id => id == _categoryId).ToHashSet());

        _unitOfWork = new Mock<IUnitOfWork>();
    }

    private ImportProductsCommandHandler Handler()
        => new(_products.Object, _categories.Object, _unitOfWork.Object, new CreateProductCommandValidator());

    private ImportProductRow Row(string sku, string name = "Imported", decimal price = 10m, Guid? categoryId = null)
        => new() { Sku = sku, Name = name, Price = price, StockQuantity = 3, CategoryId = categoryId ?? _categoryId };

    private async Task<ProductImportReport> Import(params ImportProductRow[] rows)
    {
        var result = await Handler().Handle(new ImportProductsCommand { Products = rows }, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True);
        return result.Value!;
    }

    [Test]
    public async Task GoodRows_AreCreatedAsDrafts_WithTheirFields_AndSavedOnce()
    {
        var report = await Import(Row("IMP-1"), Row("IMP-2", name: "Second", price: 25.5m));

        Assert.Multiple(() =>
        {
            Assert.That((report.Requested, report.Created, report.Failed), Is.EqualTo((2, 2, 0)));
            Assert.That(_added.Select(p => p.Sku), Is.EqualTo(new[] { "IMP-1", "IMP-2" }));
            Assert.That(_added.All(p => p.Status == ProductStatus.Draft), Is.True);
            Assert.That(_added[1].Price, Is.EqualTo(25.5m));
            Assert.That(report.Rows.Select(r => r.ProductId), Is.EqualTo(_added.Select(p => (Guid?)p.Id)));
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task ARowIsCheckedByTheCreateEndpointsOwnValidator()
    {
        // A space is outside the SKU pattern CreateProductCommandValidator enforces, and a 201-character name is over
        // its limit; neither rule is written in the import.
        var report = await Import(Row("HAS SPACE"), Row("IMP-OK", name: new string('n', 201)), Row("IMP-GOOD"));

        Assert.Multiple(() =>
        {
            Assert.That(report.Rows.Select(r => r.ErrorCode), Is.EqualTo(new[] { "Validation.Failed", "Validation.Failed", null }));
            Assert.That(report.Rows[0].Error, Does.Contain("SKU"));
            Assert.That(_added.Select(p => p.Sku), Is.EqualTo(new[] { "IMP-GOOD" }));
        });
    }

    [Test]
    public async Task ASkuOnTwoRows_RefusesBothRows_NotTheSecondOnly()
    {
        var report = await Import(Row("IMP-DUP"), Row("IMP-UNIQUE"), Row("IMP-DUP", name: "Other"));

        Assert.Multiple(() =>
        {
            Assert.That(report.Rows.Select(r => r.ErrorCode), Is.EqualTo(new[] { "Product.SkuConflict", null, "Product.SkuConflict" }));
            Assert.That(report.Rows[0].Error, Does.Contain("rows 0, 2"));
            Assert.That(_added.Select(p => p.Sku), Is.EqualTo(new[] { "IMP-UNIQUE" }));
        });
    }

    [Test]
    public async Task ASkuALiveProductHolds_IsRefused_NeverMerged()
    {
        _takenSkus.Add("IMP-TAKEN");

        var report = await Import(Row("IMP-TAKEN"), Row("IMP-FREE"));

        Assert.Multiple(() =>
        {
            Assert.That(report.Rows[0].ErrorCode, Is.EqualTo("Product.SkuConflict"));
            Assert.That(report.Rows[0].Error, Does.Contain("already exists"));
            Assert.That(_added.Select(p => p.Sku), Is.EqualTo(new[] { "IMP-FREE" }));
        });
    }

    [Test]
    public async Task AMissingCategory_RefusesItsRow()
    {
        var report = await Import(Row("IMP-NOCAT", categoryId: Guid.NewGuid()), Row("IMP-CAT"));

        Assert.Multiple(() =>
        {
            Assert.That(report.Rows[0].ErrorCode, Is.EqualTo("Category.NotFound"));
            Assert.That(_added.Select(p => p.Sku), Is.EqualTo(new[] { "IMP-CAT" }));
        });
    }

    [Test]
    public async Task TheDatabaseIsAskedOnce_ForTheWholeImport_NotPerRow()
    {
        await Import(Enumerable.Range(0, 20).Select(i => Row($"IMP-{i}")).ToArray());

        Assert.Multiple(() =>
        {
            _products.Verify(r => r.GetTakenSkusAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
            _categories.Verify(r => r.GetExistingIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
            _products.Verify(r => r.SkuExistsAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [TestCase(ImportProductsCommand.MaxRows, true)]
    [TestCase(ImportProductsCommand.MaxRows + 1, false)]
    public void TheEnvelope_IsCapped(int rows, bool valid)
    {
        var command = new ImportProductsCommand { Products = Enumerable.Range(0, rows).Select(i => Row($"R{i}")).ToList() };

        Assert.That(new ImportProductsCommandValidator().Validate(command).IsValid, Is.EqualTo(valid));
    }

    [Test]
    public void TheEnvelope_RefusesNoRowsAndANullRow()
    {
        var validator = new ImportProductsCommandValidator();

        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(new ImportProductsCommand { Products = [] }).IsValid, Is.False);
            Assert.That(validator.Validate(new ImportProductsCommand { Products = null }).IsValid, Is.False);
            Assert.That(validator.Validate(new ImportProductsCommand { Products = [Row("A"), null!] }).IsValid, Is.False);
        });
    }
}
