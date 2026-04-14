using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.ValueObjects;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EShop.Ordering.UnitTests.Infrastructure;

[TestFixture]
public class OrderRepositoryTests
{
    [Test]
    public async Task UpdateAsync_WhenNewItemIsAdded_ShouldMarkOnlyNewItemAsAdded()
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase($"OrderRepositoryTests_{Guid.NewGuid()}")
            .Options;

        await using var dbContext = new OrderingDbContext(options);
        var repository = new OrderRepository(dbContext);

        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var orderOne = Order.Create("user-1", address, [new OrderItem(Guid.NewGuid(), "Item A", 10m, 1)]);
        var orderTwo = Order.Create("user-2", address, [new OrderItem(Guid.NewGuid(), "Item B", 20m, 1)]);

        await dbContext.Orders.AddRangeAsync(orderOne, orderTwo);
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();

        var trackedOrder = await repository.GetByIdAsync(orderOne.Id, CancellationToken.None);
        Assert.That(trackedOrder, Is.Not.Null);

        // Track unrelated order items in the same DbContext scope to guard against global tracker coupling.
        _ = await dbContext.Orders.Include(x => x.Items).FirstAsync(x => x.Id == orderTwo.Id);

        var newProductId = Guid.NewGuid();
        trackedOrder!.AddItem(newProductId, "Item C", 30m, 1);

        await repository.UpdateAsync(trackedOrder, CancellationToken.None);

        var newItem = trackedOrder.Items.Single(i => i.ProductId == newProductId);
        Assert.That(dbContext.Entry(newItem).State, Is.EqualTo(EntityState.Added));

        var existingItem = trackedOrder.Items.Single(i => i.ProductName == "Item A");
        Assert.That(dbContext.Entry(existingItem).State, Is.Not.EqualTo(EntityState.Added));
    }
}
