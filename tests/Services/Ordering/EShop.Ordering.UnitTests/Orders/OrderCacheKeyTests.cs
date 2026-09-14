using EShop.BuildingBlocks.Application.Caching;
using EShop.Ordering.Application.Orders;
using EShop.Ordering.Application.Orders.Commands.AddOrderItem;
using EShop.Ordering.Application.Orders.Commands.CancelOrder;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;
using EShop.Ordering.Application.Orders.Commands.ShipOrder;
using EShop.Ordering.Application.Orders.Queries.GetOrderById;
using EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;

namespace EShop.Ordering.UnitTests.Orders;

/// <summary>
/// Audit H4: the keys a read caches under and the keys a write evicts are the same keys. The list
/// cache was never invalidated because the two drifted apart and nothing compared them.
/// </summary>
[TestFixture]
public class OrderCacheKeyTests
{
    /// <summary>
    /// Membership, not just the property. CachingBehavior versions a key only for an
    /// IVersionedCacheKey; a query that kept a CacheKeyFamily property but dropped the interface would
    /// pass a property check and cache unversioned — which is exactly the regression to catch.
    /// </summary>
    [Test]
    public void TheUserOrderList_IsCachedUnderTheFamilyWritesBump()
    {
        var query = new GetOrdersByUserQuery { UserId = "user-1", PageNumber = 3, PageSize = 25 };

        Assert.That(query, Is.InstanceOf<IVersionedCacheKey>());
        Assert.That(((IVersionedCacheKey)query).CacheKeyFamily, Is.EqualTo(OrderCacheKeys.UserOrders("user-1")));
    }

    [Test]
    public void CreatingAnOrder_BumpsItsOwnersListFamily()
    {
        ICacheInvalidatingCommand command = new CreateOrderCommand { UserId = "user-1" };

        Assert.That(command.CacheFamiliesToInvalidate, Does.Contain(OrderCacheKeys.UserOrders("user-1")));
    }

    [Test]
    public void EveryOrderWrite_EvictsTheKeyGetOrderByIdCachesUnder()
    {
        var orderId = Guid.NewGuid();
        var cached = new GetOrderByIdQuery { OrderId = orderId }.CacheKey;

        ICacheInvalidatingCommand[] writes =
        [
            new AddOrderItemCommand { OrderId = orderId },
            new RemoveOrderItemCommand { OrderId = orderId },
            new CancelOrderCommand { OrderId = orderId },
            new ShipOrderCommand { OrderId = orderId }
        ];

        foreach (var write in writes)
        {
            Assert.That(write.CacheKeysToInvalidate, Does.Contain(cached), write.GetType().Name);
        }
    }
}
