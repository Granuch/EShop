using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Application.Orders.Commands.CreateCheckedOutOrder;
using EShop.Ordering.Infrastructure.Consumers;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.Infrastructure.Repositories;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Ordering.UnitTests.Consumers;

/// <summary>
/// Ordering audit C2 and H5.
///
/// <para>
/// The previous fixture mocked <c>IMediator</c>, so no <c>Address</c> was ever built — and it asserted
/// the comma-parser's <c>Country == "Unknown"</c> as correct, a value Address rejects. These tests
/// forward to the real <see cref="CreateCheckedOutOrderCommandHandler"/> on an in-memory context, so
/// an address that cannot be stored fails here exactly as it would in production.
/// </para>
///
/// <para>
/// Every failure must <b>throw</b> — an acknowledged message is a lost checkout, since Basket has
/// already cleared the basket — and must throw an <see cref="ArgumentException"/>, which the bus's
/// retry policies skip, so it lands in the error queue at once instead of retrying for an hour.
/// </para>
/// </summary>
[TestFixture]
public class BasketCheckedOutConsumerTests
{
    private OrderingDbContext _dbContext = null!;
    private BasketCheckedOutConsumer _consumer = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContext = new OrderingDbContext(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase($"BasketCheckedOutTests_{Guid.NewGuid()}")
            .Options);

        var handler = new CreateCheckedOutOrderCommandHandler(new OrderRepository(_dbContext), _dbContext);
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(x => x.Send(It.IsAny<CreateCheckedOutOrderCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<Result<Guid>> request, CancellationToken ct) =>
                handler.Handle((CreateCheckedOutOrderCommand)request, ct));

        _consumer = ConsumerWith(mediator.Object);
    }

    [TearDown]
    public void TearDown() => _dbContext.Dispose();

    private BasketCheckedOutConsumer ConsumerWith(IMediator mediator) =>
        new(_dbContext, mediator, Mock.Of<ILogger<BasketCheckedOutConsumer>>());

    private static BasketCheckedOutEvent Checkout(
        CheckoutShippingAddress? address,
        params CheckoutItem[] items) => new()
    {
        UserId = "user-1",
        TotalPrice = items.Sum(i => i.Price * i.Quantity),
        ShippingAddressDetails = address,
        ShippingAddress = "display only, never parsed",
        PaymentMethod = "card",
        Items = items.Length > 0
            ? [.. items]
            : [new CheckoutItem { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 20.00m, Quantity = 2 }]
    };

    private static CheckoutShippingAddress Kyiv(string country = "UA") => new()
    {
        Street = "1 Khreshchatyk St",
        City = "Kyiv",
        State = "Kyiv",
        ZipCode = "01001",
        Country = country
    };

    private static Mock<ConsumeContext<T>> ContextFor<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.Setup(x => x.Message).Returns(message);
        context.Setup(x => x.MessageId).Returns(Guid.NewGuid());
        context.Setup(x => x.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private int StoredOrders => _dbContext.Orders.Count();
    private int StoredClaims => _dbContext.Set<ProcessedMessage>().Count();

    /// <summary>
    /// A non-US address: the free-text parser could not produce one of these, because it needed
    /// exactly five comma-separated parts ending in an ISO code.
    /// </summary>
    [Test]
    public async Task AStructuredAddress_BecomesTheOrdersAddress()
    {
        await _consumer.Consume(ContextFor(Checkout(Kyiv())).Object);

        var order = _dbContext.Orders.Include(o => o.Items).Single();
        Assert.That(order.ShippingAddress.Street, Is.EqualTo("1 Khreshchatyk St"));
        Assert.That(order.ShippingAddress.City, Is.EqualTo("Kyiv"));
        Assert.That(order.ShippingAddress.ZipCode, Is.EqualTo("01001"));
        Assert.That(order.ShippingAddress.Country, Is.EqualTo("UA"));
        Assert.That(order.TotalPrice, Is.EqualTo(40.00m));
        Assert.That(StoredClaims, Is.EqualTo(1), "a consumed message is claimed");
    }

    [Test]
    public void AMessageWithoutAStructuredAddress_GoesStraightToTheErrorQueue()
    {
        var ex = Assert.ThrowsAsync<InvalidCheckoutEventException>(() =>
            _consumer.Consume(ContextFor(Checkout(address: null)).Object));

        Assert.That(ex, Is.InstanceOf<ArgumentException>(), "ArgumentException is what the retry policy skips");
        Assert.That(ex!.Message, Does.Contain("ShippingAddressDetails"));
        Assert.That(StoredOrders, Is.EqualTo(0));
        Assert.That(StoredClaims, Is.EqualTo(0), "a failed message must not stay claimed, or a redelivery is skipped");
    }

    /// <summary>
    /// Address throws ArgumentException itself, so an address Ordering cannot store is also not retried.
    /// Basket's validator mirrors Address to keep this from happening in the first place.
    /// </summary>
    [Test]
    public void AnAddressOrderingCannotStore_IsNotRetried()
    {
        Assert.ThrowsAsync(Is.InstanceOf<ArgumentException>(), () =>
            _consumer.Consume(ContextFor(Checkout(Kyiv(country: "Ukraine"))).Object));

        Assert.That(StoredOrders, Is.EqualTo(0));
    }

    /// <summary>
    /// Audit H5. Validation failures come back as a Result, not an exception; the consumer used to log
    /// one and return, so the claim committed and the checkout was acknowledged and gone.
    /// </summary>
    [Test]
    public void ARejectedOrder_IsNotAcknowledged()
    {
        var rejecting = new Mock<IMediator>();
        rejecting
            .Setup(x => x.Send(It.IsAny<CreateCheckedOutOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure(new Error("Validation.Failed", "Order must have at least one item")));

        var ex = Assert.ThrowsAsync<InvalidCheckoutEventException>(() =>
            ConsumerWith(rejecting.Object).Consume(ContextFor(Checkout(Kyiv())).Object));

        Assert.That(ex!.Message, Does.Contain("Validation.Failed"));
        Assert.That(StoredClaims, Is.EqualTo(0));
    }

    [Test]
    public void AnOrderTheDomainRefuses_IsNotRetried()
    {
        var productId = Guid.NewGuid();
        var message = Checkout(
            Kyiv(),
            new CheckoutItem { ProductId = productId, ProductName = "Widget", Price = 10m, Quantity = 1 },
            new CheckoutItem { ProductId = productId, ProductName = "Widget", Price = 10m, Quantity = 2 });

        var ex = Assert.ThrowsAsync<InvalidCheckoutEventException>(() =>
            _consumer.Consume(ContextFor(message).Object));

        Assert.That(ex!.InnerException, Is.InstanceOf<DomainException>());
        Assert.That(StoredOrders, Is.EqualTo(0));
    }
}
