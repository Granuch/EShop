using System.Runtime.CompilerServices;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Administration;
using EShop.Catalog.Application.Administration.Commands.InvalidateCacheFamilies;
using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Products;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EShop.Catalog.UnitTests.Administration;

/// <summary>Admin panel S19, endpoint #88: the cache lever's command, validator and family list.</summary>
[TestFixture]
public class InvalidateCacheFamiliesTests
{
    // ---------- the family list ----------

    [Test]
    public void EveryFamilyACatalogQueryWritesInto_CanBeInvalidated()
    {
        // The endpoint cannot discover families (no SCAN), so a query that joins a new family without it being added to
        // CatalogCacheFamilies.All would be silently skipped by "invalidate everything". Every family here is a constant
        // returned by an expression-bodied property, so an uninitialized instance reads it without running a constructor.
        var written = typeof(CatalogCacheFamilies).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IVersionedCacheKey).IsAssignableFrom(t))
            .Select(t => ((IVersionedCacheKey)RuntimeHelpers.GetUninitializedObject(t)).CacheKeyFamily)
            .Distinct()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.Not.Empty, "the scan found no versioned query, so it is not looking where the queries are");
            Assert.That(written, Is.SubsetOf(CatalogCacheFamilies.All));
            Assert.That(CatalogCacheFamilies.All, Is.Unique);
        });
    }

    // ---------- the validator ----------

    [TestCase(null)]
    [TestCase(ProductCacheFamilies.ProductList)]
    [TestCase(CategoryCacheFamilies.CategoryList)]
    public void AKnownFamily_OrNone_IsAccepted(string? family)
        => Assert.That(Validate(family).IsValid, Is.True);

    [TestCase("Products:List")]
    [TestCase("PRODUCTS:LIST")]
    [TestCase("products")]
    [TestCase("")]
    [TestCase("products:list ")]
    public void AnythingElse_IsRefused(string family)
        => Assert.That(Validate(family).IsValid, Is.False);

    private static FluentValidation.Results.ValidationResult Validate(string? family)
        => new InvalidateCacheFamiliesCommandValidator().Validate(new InvalidateCacheFamiliesCommand { Family = family });

    // ---------- the handler ----------

    [Test]
    public async Task NoFamily_BumpsEveryFamily_InOrder_AndReportsThem()
    {
        var versions = new Mock<ICacheKeyVersionProvider>();
        var bumped = new List<string>();
        versions.Setup(v => v.BumpVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((f, _) => bumped.Add(f))
            .Returns(Task.CompletedTask);

        var result = await Handler(versions.Object).Handle(new InvalidateCacheFamiliesCommand(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(bumped, Is.EqualTo(CatalogCacheFamilies.All));
            Assert.That(result.Value!.Families, Is.EqualTo(CatalogCacheFamilies.All));
            Assert.That(result.Value.Service, Is.EqualTo("catalog"));
        });
    }

    [Test]
    public async Task OneFamily_BumpsOnlyThatFamily()
    {
        var versions = new Mock<ICacheKeyVersionProvider>();

        var result = await Handler(versions.Object).Handle(
            new InvalidateCacheFamiliesCommand { Family = CategoryCacheFamilies.CategoryList }, CancellationToken.None);

        Assert.That(result.Value!.Families, Is.EqualTo(new[] { CategoryCacheFamilies.CategoryList }));
        versions.Verify(v => v.BumpVersionAsync(CategoryCacheFamilies.CategoryList, It.IsAny<CancellationToken>()), Times.Once);
        versions.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AFailedBump_IsReportedAsCacheUnavailable_NotAsSuccess()
    {
        // The reason this command does not use ICacheInvalidatingCommand: that marker logs a failed bump and lets the
        // request succeed, which is right for a write and wrong for a command whose only effect is the bump.
        var versions = new Mock<ICacheKeyVersionProvider>();
        versions.Setup(v => v.BumpVersionAsync(CategoryCacheFamilies.CategoryList, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis-secret-host:6379 refused"));

        var result = await Handler(versions.Object).Handle(new InvalidateCacheFamiliesCommand(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error!.Code, Is.EqualTo(InvalidateCacheFamiliesCommandHandler.CacheUnavailableCode));
            Assert.That(result.Error.Message, Does.Contain($"Invalidated {ProductCacheFamilies.ProductList}"));
            Assert.That(result.Error.Message, Does.Not.Contain("redis-secret-host"));
        });
    }

    [Test]
    public void ACancelledRequest_IsNotReportedAsAnUnavailableCache()
    {
        var versions = new Mock<ICacheKeyVersionProvider>();
        versions.Setup(v => v.BumpVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        Assert.ThrowsAsync<OperationCanceledException>(() =>
            Handler(versions.Object).Handle(new InvalidateCacheFamiliesCommand(), CancellationToken.None));
    }

    private static InvalidateCacheFamiliesCommandHandler Handler(ICacheKeyVersionProvider versions)
        => new(versions, NullLogger<InvalidateCacheFamiliesCommandHandler>.Instance);

    // ---------- audit ----------

    [Test]
    public void EachFamilyBumped_IsItsOwnAuditItem()
    {
        IAuditedCommand command = new InvalidateCacheFamiliesCommand();
        var report = new CacheInvalidationReport("catalog", CatalogCacheFamilies.All);

        Assert.Multiple(() =>
        {
            Assert.That(command.AuditEntityType, Is.EqualTo("CacheFamily"));
            Assert.That(command.AuditEntityId, Is.Null);
            Assert.That(command.AuditItemsFromResult(report)!.Select(i => i.EntityId), Is.EqualTo(CatalogCacheFamilies.All));
            Assert.That(command.AuditItemsFromResult(report)!.Select(i => i.ErrorCode), Is.All.Null);
        });
    }
}
