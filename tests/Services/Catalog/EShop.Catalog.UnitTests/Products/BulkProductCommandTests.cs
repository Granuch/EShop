using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Application.Products.Bulk;
using EShop.Catalog.Application.Products.Commands.BulkChangeProductCategory;
using EShop.Catalog.Application.Products.Commands.BulkDeleteProducts;
using EShop.Catalog.Application.Products.Commands.BulkPublishProducts;
using EShop.Catalog.Application.Products.Commands.BulkUpdateProductPrices;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Moq;

namespace EShop.Catalog.UnitTests.Products;

/// <summary>
/// Admin panel S16. The bulk commands before anything reaches a database: the caps, the refusals that apply to the
/// whole request, what each command declares for cache invalidation and audit, and the per-product loop.
/// </summary>
[TestFixture]
public class BulkProductCommandTests
{
    private static List<Guid> Ids(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    // ---------- validation ----------

    [TestCase(1, true)]
    [TestCase(BulkProductLimits.MaxItemsPerRequest, true)]
    [TestCase(BulkProductLimits.MaxItemsPerRequest + 1, false)]
    public void TheIdList_IsCapped_NotTruncated(int count, bool valid)
    {
        var result = new BulkPublishProductsCommandValidator().Validate(new BulkPublishProductsCommand { ProductIds = Ids(count) });

        Assert.That(result.IsValid, Is.EqualTo(valid));
    }

    [Test]
    public void AnEmptyOrMissingIdList_IsRefused()
    {
        var validator = new BulkDeleteProductsCommandValidator();

        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(new BulkDeleteProductsCommand { ProductIds = [] }).IsValid, Is.False);
            Assert.That(validator.Validate(new BulkDeleteProductsCommand { ProductIds = null }).IsValid, Is.False);
        });
    }

    [Test]
    public void ARepeatedId_IsRefused_RatherThanDeduplicated()
    {
        var id = Guid.NewGuid();
        var result = new BulkPublishProductsCommandValidator().Validate(new BulkPublishProductsCommand { ProductIds = [id, id] });

        Assert.That(result.Errors.Select(e => e.ErrorMessage), Has.Some.Contains("only once"));
    }

    [Test]
    public void AnEmptyId_IsRefused()
    {
        var result = new BulkPublishProductsCommandValidator().Validate(
            new BulkPublishProductsCommand { ProductIds = [Guid.NewGuid(), Guid.Empty] });

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void TheCategoryMove_NeedsACategory()
    {
        var result = new BulkChangeProductCategoryCommandValidator().Validate(
            new BulkChangeProductCategoryCommand { ProductIds = Ids(1), CategoryId = Guid.Empty });

        Assert.That(result.Errors.Select(e => e.PropertyName), Does.Contain(nameof(BulkChangeProductCategoryCommand.CategoryId)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void APriceMustBePositive(decimal price)
    {
        var result = new BulkUpdateProductPricesCommandValidator().Validate(new BulkUpdateProductPricesCommand
        {
            Items = [new BulkProductPriceItem { ProductId = Guid.NewGuid(), Price = price }]
        });

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ThePriceItems_FollowTheIdRules()
    {
        var id = Guid.NewGuid();
        var validator = new BulkUpdateProductPricesCommandValidator();

        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(new BulkUpdateProductPricesCommand
            {
                Items = [new BulkProductPriceItem { ProductId = id, Price = 1 }, new BulkProductPriceItem { ProductId = id, Price = 2 }]
            }).IsValid, Is.False, "two prices for one product have no right answer");

            Assert.That(validator.Validate(new BulkUpdateProductPricesCommand
            {
                Items = Ids(BulkProductLimits.MaxItemsPerRequest + 1).Select(i => new BulkProductPriceItem { ProductId = i, Price = 1 }).ToList()
            }).IsValid, Is.False, "the cap");

            Assert.That(validator.Validate(new BulkUpdateProductPricesCommand { Items = [null!] }).IsValid, Is.False,
                "a JSON null item is refused, not dereferenced");
        });
    }

    // ---------- what the commands declare ----------

    [Test]
    public void EveryBulkCommand_BumpsTheListFamilyOnce_AndEvictsBothDetailVariantsOfEachProduct()
    {
        var ids = Ids(3);
        ICacheInvalidatingCommand command = new BulkDeleteProductsCommand { ProductIds = ids };

        Assert.Multiple(() =>
        {
            Assert.That(command.CacheFamiliesToInvalidate, Is.EqualTo(new[] { ProductCacheFamilies.ProductList }));
            Assert.That(command.CacheKeysToInvalidate, Is.EquivalentTo(ids.SelectMany(ProductCacheKeys.AllDetailVariants)));
        });
    }

    [Test]
    public void AnOverCapRequest_DeclaresNoKeys_BecauseItChangesNothing()
    {
        ICacheInvalidatingCommand command = new BulkPublishProductsCommand { ProductIds = Ids(BulkProductLimits.MaxItemsPerRequest + 1) };

        Assert.That(command.CacheKeysToInvalidate, Is.Empty);
    }

    [Test]
    public void APriceBatch_AuditsEachProduct_WithThePriceItWasSent()
    {
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        IAuditedCommand command = new BulkUpdateProductPricesCommand
        {
            Items = [new BulkProductPriceItem { ProductId = first, Price = 12.5m }, new BulkProductPriceItem { ProductId = second, Price = 99m }]
        };
        var report = BulkProductReport.From([
            BulkProductItemResult.Success(first),
            BulkProductItemResult.Failure(second, BulkProductProcessor.DomainErrorCode, "below the discount")
        ]);

        var items = command.AuditItemsFromResult(report)!;

        Assert.Multiple(() =>
        {
            Assert.That(command.AuditEntityId, Is.Null);
            Assert.That(items.Select(i => i.EntityId), Is.EqualTo(new[] { first.ToString(), second.ToString() }));
            Assert.That(items.Select(i => i.ErrorCode), Is.EqualTo(new[] { null, BulkProductProcessor.DomainErrorCode }));
            Assert.That(items[0].Detail!.GetType().GetProperty("Price")!.GetValue(items[0].Detail), Is.EqualTo(12.5m));
        });
    }

    // ---------- the per-product loop ----------

    [Test]
    public async Task TheReport_FollowsRequestOrder_AndNamesMissingProducts()
    {
        var product = Product.Create("A", "BULK-A", 10m, 1, Guid.NewGuid());
        var missing = Guid.NewGuid();
        var (repository, unitOfWork) = RepositoryHolding(product);

        var report = await BulkProductProcessor.ApplyAsync(
            [missing, product.Id], repository.Object, unitOfWork.Object, p => p.Publish(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(report.Items.Select(i => i.ProductId), Is.EqualTo(new[] { missing, product.Id }));
            Assert.That(report.Items[0].ErrorCode, Is.EqualTo("Product.NotFound"));
            Assert.That(report.Items[1].Succeeded, Is.True);
            Assert.That((report.Requested, report.Succeeded, report.Failed), Is.EqualTo((2, 1, 1)));
            Assert.That(product.Status, Is.EqualTo(ProductStatus.Active));
        });
    }

    [Test]
    public async Task ADomainRefusal_IsThatProductsRow_AndTheRestGoOn()
    {
        var discounted = Product.Create("Discounted", "BULK-D", 100m, 1, Guid.NewGuid());
        discounted.SetDiscountPrice(80m);
        var plain = Product.Create("Plain", "BULK-P", 100m, 1, Guid.NewGuid());
        var (repository, unitOfWork) = RepositoryHolding(discounted, plain);

        var report = await BulkProductProcessor.ApplyAsync(
            [discounted.Id, plain.Id], repository.Object, unitOfWork.Object, p => p.UpdatePrice(50m), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(report.Items[0].ErrorCode, Is.EqualTo(BulkProductProcessor.DomainErrorCode));
            Assert.That(report.Items[0].Error, Does.Contain("discount"), "the domain's own message");
            Assert.That(discounted.Price, Is.EqualTo(100m), "refused before anything was assigned");
            Assert.That(plain.Price, Is.EqualTo(50m));
        });
    }

    [Test]
    public async Task TheBatch_IsSavedOnce_NotPerProduct()
    {
        var products = Enumerable.Range(0, 5).Select(i => Product.Create($"P{i}", $"BULK-{i}", 10m, 1, Guid.NewGuid())).ToArray();
        var (repository, unitOfWork) = RepositoryHolding(products);

        await BulkProductProcessor.ApplyAsync(
            products.Select(p => p.Id).ToList(), repository.Object, unitOfWork.Object, p => p.SoftDelete(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            repository.Verify(r => r.GetByIdsWithoutChildrenAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public async Task AMissingTargetCategory_RefusesTheWholeRequest_BeforeAnyProductIsLoaded()
    {
        var products = new Mock<IProductRepository>();
        var categories = new Mock<ICategoryRepository>();
        categories.Setup(c => c.GetExistingIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var unitOfWork = new Mock<IUnitOfWork>();

        var result = await new BulkChangeProductCategoryCommandHandler(products.Object, categories.Object, unitOfWork.Object)
            .Handle(new BulkChangeProductCategoryCommand { ProductIds = Ids(2), CategoryId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error!.Code, Is.EqualTo("Category.NotFound"));
            products.Verify(r => r.GetByIdsWithoutChildrenAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
            unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    private static (Mock<IProductRepository>, Mock<IUnitOfWork>) RepositoryHolding(params Product[] products)
    {
        var repository = new Mock<IProductRepository>();
        repository.Setup(r => r.GetByIdsWithoutChildrenAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => products.Where(p => ids.Contains(p.Id)).ToList());
        var unitOfWork = new Mock<IUnitOfWork>();
        return (repository, unitOfWork);
    }
}
