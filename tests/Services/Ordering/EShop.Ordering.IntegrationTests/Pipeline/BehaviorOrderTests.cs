using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Pipeline;

/// <summary>
/// Audit M13. Pins the MediatR pipeline order, which is set purely by the order of three <c>Add*</c>
/// calls in <c>Program.cs</c> and is otherwise unobservable: every functional test stays green with it
/// wrong. Ordering's order was correct but guarded by prose alone; Catalog's
/// <c>Pipeline/BehaviorOrderTests</c> is the precedent.
///
/// <para>
/// Two distinct defects turn this red. CacheInvalidation registered after Application puts it inside
/// the transaction, so it evicts and bumps family versions before the write commits (Catalog audit C2).
/// Infrastructure registered before Application puts Caching ahead of Validation, so a query is
/// answered from cache without being validated.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class BehaviorOrderTests : IntegrationTestBase
{
    [Test]
    public void ThePipelineRuns_CacheInvalidation_Transaction_Validation_ThenCaching()
    {
        using var scope = Factory.Services.CreateScope();

        // MediatR resolves behaviors in registration order and wraps them outermost-first, so the
        // sequence this returns IS the pipeline. The behaviors are open generics, so one request type
        // shows all of them; each decides at run time whether it applies.
        var order = scope.ServiceProvider
            .GetServices<IPipelineBehavior<CreateOrderCommand, Result<Guid>>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ToList();

        var invalidation = order.IndexOf(typeof(CacheInvalidationBehavior<,>));
        var transaction = order.IndexOf(typeof(TransactionBehavior<,>));
        var validation = order.IndexOf(typeof(ValidationBehavior<,>));
        var caching = order.IndexOf(typeof(CachingBehavior<,>));

        invalidation.Should().BeGreaterThanOrEqualTo(0, "CacheInvalidationBehavior must be registered");
        transaction.Should().BeGreaterThanOrEqualTo(0, "TransactionBehavior must be registered");
        validation.Should().BeGreaterThanOrEqualTo(0, "ValidationBehavior must be registered");
        caching.Should().BeGreaterThanOrEqualTo(0, "CachingBehavior must be registered");

        invalidation.Should().BeLessThan(transaction,
            "CacheInvalidationBehavior invalidates after the handler returns, so it must be OUTSIDE "
            + "TransactionBehavior — inside, it evicts before the write commits and a concurrent read "
            + "caches the old state for the full TTL");

        transaction.Should().BeLessThan(validation,
            "Transaction before Validation is the repo-wide ordering; this test is not licence to "
            + "reorder the Application registration");

        validation.Should().BeLessThan(caching,
            "CachingBehavior ahead of ValidationBehavior would answer an invalid query from the cache");
    }
}
