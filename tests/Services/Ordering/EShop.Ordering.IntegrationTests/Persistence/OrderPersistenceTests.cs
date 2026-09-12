using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Persistence;

/// <summary>
/// Relational behaviour that needed PostgreSQL to be observed at all (Ordering audit M11): the
/// concurrency token and the per-user list's index.
/// </summary>
[TestFixture]
[Category("Integration")]
public class OrderPersistenceTests : IntegrationTestBase
{
    /// <summary>
    /// Audit L4. Two edits of one order made from the same starting state, each adding the same product
    /// at price 0 — so neither changes any column of the order itself. The order's Version was bumped only
    /// when its own row changed, so both commits went through and the order stored two lines for one
    /// product. Now the second is a concurrency conflict (409 over HTTP) and one line is stored.
    /// </summary>
    [Test]
    public async Task TwoConcurrentEditsThatOnlyTouchItems_Conflict_AndOnlyOneIsStored()
    {
        Guid orderId;
        using (var scope = CreateScope())
        {
            orderId = (await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, userId: $"conc-{Guid.NewGuid():N}")).Id;
        }

        using var first = Factory.Services.CreateScope();
        using var second = Factory.Services.CreateScope();
        var firstRepository = first.ServiceProvider.GetRequiredService<IOrderRepository>();
        var secondRepository = second.ServiceProvider.GetRequiredService<IOrderRepository>();

        var firstCopy = (await firstRepository.GetByIdAsync(orderId))!;
        var secondCopy = (await secondRepository.GetByIdAsync(orderId))!;

        var productId = Guid.NewGuid();
        firstCopy.AddItem(productId, "Free sample", 0m, 1);
        secondCopy.AddItem(productId, "Free sample", 0m, 1);

        await firstRepository.UpdateAsync(firstCopy);
        await first.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync();

        await secondRepository.UpdateAsync(secondCopy);
        var secondSave = () => second.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync();

        await secondSave.Should().ThrowAsync<DbUpdateConcurrencyException>();

        using var check = Factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<OrderingDbContext>()
                .Set<OrderItem>().CountAsync(i => i.ProductId == productId))
            .Should().Be(1);
    }

    /// <summary>
    /// Audit L12. The per-user list filters on UserId and pages by (CreatedAt, Id) descending; the
    /// migrated schema carries one index in exactly that shape.
    /// </summary>
    [Test]
    public async Task ThePerUserList_HasACompositeIndexInItsSortOrder()
    {
        using var scope = CreateScope();
        var definitions = await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'Orders'")
            .ToListAsync();

        definitions.Should().Contain(d => d.Contains("(\"UserId\", \"CreatedAt\" DESC, \"Id\" DESC)"));
    }
}
