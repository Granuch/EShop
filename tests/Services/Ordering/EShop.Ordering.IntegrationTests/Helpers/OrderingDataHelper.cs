using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.ValueObjects;
using EShop.Ordering.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Helpers;

/// <summary>
/// Helper methods for creating test data in integration tests.
/// </summary>
public static class OrderingDataHelper
{
    public static async Task<Order> CreateOrderAsync(
        IServiceProvider serviceProvider,
        string userId = "test-user-1",
        string street = "123 Test St",
        string city = "TestCity",
        string country = "US")
    {
        var db = serviceProvider.GetRequiredService<OrderingDbContext>();

        var address = new Address(street, city, "TS", "12345", country);
        var items = new List<OrderItem>
        {
            new(Guid.NewGuid(), "Test Product", 19.99m, 1)
        };

        var order = Order.Create(userId, address, items);
        order.ClearDomainEvents();

        await db.Orders.AddAsync(order);
        await db.SaveChangesAsync();

        return order;
    }

    public static async Task<Order> CreatePaidOrderAsync(IServiceProvider serviceProvider, string userId = "test-user-1")
    {
        var db = serviceProvider.GetRequiredService<OrderingDbContext>();

        var address = new Address("123 Test St", "TestCity", "TS", "12345", "US");
        var items = new List<OrderItem>
        {
            new(Guid.NewGuid(), "Test Product", 29.99m, 2)
        };

        var order = Order.Create(userId, address, items);
        order.MarkAsPaid($"pi_{Guid.NewGuid():N}");
        order.ClearDomainEvents();

        await db.Orders.AddAsync(order);
        await db.SaveChangesAsync();

        return order;
    }

    public static async Task<Guid> GetFirstOrderIdAsync(IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<OrderingDbContext>();
        var order = db.Orders.FirstOrDefault()
            ?? throw new InvalidOperationException("No orders found in test database");
        return order.Id;
    }
}
