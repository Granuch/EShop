using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Pipeline;

/// <summary>
/// C2. Pins the MediatR pipeline order, which is set purely by the order of three
/// <c>Add*</c> calls in <c>Program.cs</c> and is otherwise unobservable.
///
/// <para>
/// This is the one part of the caching design that <b>nothing else can catch</b>. The defect it
/// guards — <c>CacheInvalidationBehavior</c> registered inside <c>TransactionBehavior</c>, so
/// eviction and DEBT-16 family bumps happen before the write commits — produces no failure, no
/// log, and no test failure. It needs a concurrent reader hitting a sub-millisecond window against
/// a shared cache to show itself, which no test in this repo can arrange. Every functional test
/// stays green with the order wrong, which is exactly how all four services shipped it.
/// </para>
///
/// <para>
/// So this asserts the structure rather than the symptom. Swapping the calls back — or
/// "simplifying" <c>AddEShopCacheInvalidation()</c> into <c>AddCatalogInfrastructure</c>, which is
/// registered after Application and therefore inside the transaction — turns this red.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class BehaviorOrderTests : IntegrationTestBase
{
    [Test]
    public void CacheInvalidationRunsOutsideTheTransaction()
    {
        using var scope = Factory.Services.CreateScope();

        // MediatR resolves behaviors in registration order and wraps them outermost-first, so the
        // sequence this returns IS the pipeline.
        var order = scope.ServiceProvider
            .GetServices<IPipelineBehavior<CreateProductCommand, Result<Guid>>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ToList();

        var invalidation = order.IndexOf(typeof(CacheInvalidationBehavior<,>));
        var transaction = order.IndexOf(typeof(TransactionBehavior<,>));
        var validation = order.IndexOf(typeof(ValidationBehavior<,>));

        invalidation.Should().BeGreaterThanOrEqualTo(0, "CacheInvalidationBehavior must be registered");
        transaction.Should().BeGreaterThanOrEqualTo(0, "TransactionBehavior must be registered");

        invalidation.Should().BeLessThan(transaction,
            "CacheInvalidationBehavior invalidates after the handler returns, so it must be OUTSIDE "
            + "TransactionBehavior — inside, it evicts keys and bumps family versions before the "
            + "write commits, and a concurrent read then caches pre-commit data for the full TTL");

        transaction.Should().BeLessThan(validation,
            "Transaction before Validation is the Stage 7 ordering; this test must not be read as "
            + "licence to reorder the Application registration either");
    }
}
