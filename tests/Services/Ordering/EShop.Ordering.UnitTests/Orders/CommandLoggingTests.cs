using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Orders.Commands.CreateCheckedOutOrder;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;

namespace EShop.Ordering.UnitTests.Orders;

/// <summary>
/// Audit L7. LoggingBehavior writes every command at Information and redacts a property only when it
/// carries [SensitiveData] or its name is on a fixed list of secret names — and no address field is on
/// that list. So without the attribute every order logged the customer's home address in clear text.
/// </summary>
[TestFixture]
public class CommandLoggingTests
{
    private static readonly string[] AddressFields = ["Street", "City", "State", "ZipCode"];

    [TestCase(typeof(CreateOrderCommand))]
    [TestCase(typeof(CreateCheckedOutOrderCommand))]
    public void AddressFields_AreRedactedFromTheCommandLog(Type command)
    {
        foreach (var field in AddressFields)
        {
            var property = command.GetProperty(field);
            Assert.That(property, Is.Not.Null, $"{command.Name}.{field}");
            Assert.That(property!.IsDefined(typeof(SensitiveDataAttribute), inherit: true), Is.True,
                $"{command.Name}.{field} must carry [SensitiveData]");
        }
    }
}
