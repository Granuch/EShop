using EShop.BuildingBlocks.Application.Auditing;
using MediatR;
using EShop.Ordering.Application.Orders.Commands.ShipOrder;

namespace EShop.Ordering.UnitTests.Audit;

/// <summary>
/// Admin panel S15. Every command in Ordering's Application assembly is classified — audited or not, with a reason — so a new
/// command cannot ship without someone deciding whether its executions belong in <c>audit_log</c>.
///
/// <para>
/// The rule (see <see cref="IAuditedCommand"/>): a command is audited when an admin-authorized endpoint can send it,
/// including owner-or-admin ones. The lists are the decision; the tests hold the code to them in both directions, so
/// dropping the marker from a command and marking one that is listed as not audited both go red.
/// </para>
/// </summary>
[TestFixture]
public class AuditedCommandClassificationTests
{
    private static readonly string[] Audited =
    [
        "AddOrderItemCommand",
        "AddOrderNoteCommand",
        "CancelOrderCommand",
        "DeliverOrderCommand",
        "RemoveOrderItemCommand",
        "ShipOrderCommand",
        "UpdateOrderItemQuantityCommand",
        "UpdateShippingAddressCommand",
    ];

    /// <summary>Command → why its executions are not audited.</summary>
    private static readonly Dictionary<string, string> NotAudited = new(StringComparer.Ordinal)
    {
        ["CreateCheckedOutOrderCommand"] = "sent by BasketCheckedOutConsumer, not by a person",
        ["CreateOrderCommand"] = "a customer placing an order; no admin endpoint sends it",
    };

    private static readonly Type[] Requests = typeof(ShipOrderCommand).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false })
        .Where(t => t.GetInterfaces().Any(i =>
            i == typeof(IRequest) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))))
        .ToArray();

    [Test]
    public void EveryRequest_IsNamedAsACommandOrAQuery()
        => Assert.That(
            Requests.Where(t => !t.Name.EndsWith("Command", StringComparison.Ordinal)
                                && !t.Name.EndsWith("Query", StringComparison.Ordinal)).Select(t => t.Name),
            Is.Empty,
            "the classification below finds commands by name, so a request named otherwise would escape it");

    [Test]
    public void EveryCommand_IsClassified_ExactlyOnce()
    {
        var commands = Requests.Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal)).Select(t => t.Name);

        Assert.Multiple(() =>
        {
            Assert.That(Audited.Intersect(NotAudited.Keys), Is.Empty, "a command is audited or not, never both");
            Assert.That(commands, Is.EquivalentTo(Audited.Concat(NotAudited.Keys)),
                "a new command must be added to Audited or NotAudited — decide whether its executions are audited");
        });
    }

    [Test]
    public void TheAuditedCommands_AreExactlyTheOnesCarryingTheMarker()
        => Assert.That(
            Requests.Where(t => typeof(IAuditedCommand).IsAssignableFrom(t)).Select(t => t.Name),
            Is.EquivalentTo(Audited));
}
