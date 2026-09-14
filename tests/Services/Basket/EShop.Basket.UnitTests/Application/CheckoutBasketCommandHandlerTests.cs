using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Commands.CheckoutBasket;
using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

/// <summary>
/// Basket audit S3 (C2, H1, H2, M7; D2, D3). The handler's decisions — what is a repeat, what is a conflict, which id is
/// returned. What the commit actually writes is pinned against real Redis in the integration suite's
/// <c>Checkout/CheckoutTests</c>.
/// </summary>
[TestFixture]
public class CheckoutBasketCommandHandlerTests
{
    private static readonly CheckoutAddress ValidAddress = new()
    {
        Street = "1 Main St",
        City = "Springfield",
        State = "IL",
        ZipCode = "62701",
        Country = "US"
    };

    private Mock<IBasketRepository> _repository = null!;
    private Mock<IBasketCheckoutStore> _store = null!;
    private Mock<IProductCatalogReader> _catalog = null!;
    private Mock<IBasketMetrics> _metrics = null!;
    private CheckoutBasketCommandHandler _handler = null!;
    private BasketCheckedOutEvent? _committed;

    [SetUp]
    public void SetUp()
    {
        _committed = null;

        _repository = new Mock<IBasketRepository>();

        _store = new Mock<IBasketCheckoutStore>();
        _store
            .Setup(x => x.TryBeginProcessingAsync("user-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _store
            .Setup(x => x.CommitAsync(It.IsAny<ShoppingBasket>(), It.IsAny<BasketCheckedOutEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ShoppingBasket, BasketCheckedOutEvent, CancellationToken>((_, e, _) => _committed = e)
            .ReturnsAsync(true);

        _metrics = new Mock<IBasketMetrics>();
        _metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        // By default Catalog agrees with every stored line (StoredBasket prices at 10, quantity 1).
        _catalog = new Mock<IProductCatalogReader>();
        _catalog
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new ProductCatalogSnapshot(id, "Product", 10m, 100));

        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.SetupGet(x => x.CorrelationId).Returns("corr-1");

        _handler = new CheckoutBasketCommandHandler(
            _repository.Object,
            _store.Object,
            _catalog.Object,
            currentUser.Object,
            Mock.Of<ILogger<CheckoutBasketCommandHandler>>(),
            _metrics.Object);
    }

    private static CheckoutBasketCommand Command() => new()
    {
        UserId = "user-1",
        ShippingAddress = ValidAddress,
        PaymentMethod = "Card"
    };

    private static ShoppingBasket StoredBasket(int items = 1) => ShoppingBasket.Rehydrate(
        "user-1",
        DateTime.UtcNow,
        DateTime.UtcNow,
        Enumerable.Range(0, items).Select(_ => new StoredBasketItem(Guid.NewGuid(), "Product", 10m, 1)).ToArray(),
        concurrencyToken: "stored-state");

    private void BasketIs(ShoppingBasket? basket)
        => _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);

    private void LastCheckoutWas(Guid? checkoutId)
        => _store.Setup(x => x.GetCompletedCheckoutIdAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(checkoutId);

    private void CommitIsRefused()
        => _store
            .Setup(x => x.CommitAsync(It.IsAny<ShoppingBasket>(), It.IsAny<BasketCheckedOutEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

    private void VerifyNothingCommitted()
        => _store.Verify(
            x => x.CommitAsync(It.IsAny<ShoppingBasket>(), It.IsAny<BasketCheckedOutEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

    [Test]
    public async Task ABasket_IsCheckedOut_EvenWhenTheUserCompletedACheckoutMinutesAgo()
    {
        var earlierCheckout = Guid.NewGuid();
        BasketIs(StoredBasket());
        LastCheckoutWas(earlierCheckout);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_committed, Is.Not.Null, "an existing basket must be committed, not answered with the last checkout (C2)");
        Assert.That(result.Value, Is.Not.EqualTo(earlierCheckout));
        _metrics.Verify(x => x.RecordCheckout("success"), Times.Once);
    }

    [Test]
    public async Task TheCheckoutId_IsTheIdOfTheEventCommitted_AndTheEventCarriesTheRequestCorrelation()
    {
        BasketIs(StoredBasket());

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Value, Is.EqualTo(_committed!.EventId),
            "D3: the checkoutId is the id the outbox publishes as the MessageId Ordering deduplicates on");
        Assert.That(_committed.CorrelationId, Is.EqualTo("corr-1"));
        Assert.That(_committed.UserId, Is.EqualTo("user-1"));
    }

    [Test]
    public async Task ARetryAfterTheBasketIsGone_ReturnsTheCompletedCheckoutsId_AndCommitsNothing()
    {
        var completedCheckout = Guid.NewGuid();
        BasketIs(null);
        LastCheckoutWas(completedCheckout);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(completedCheckout));
        VerifyNothingCommitted();
        _metrics.Verify(x => x.RecordCheckout("deduplicated"), Times.Once);
    }

    [Test]
    public async Task NoBasket_AndNoCompletedCheckout_IsBasketEmpty()
    {
        BasketIs(null);
        LastCheckoutWas(null);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketEmpty));
        VerifyNothingCommitted();
    }

    [Test]
    public async Task ABasketWithNoItems_IsBasketEmpty_EvenWithACompletedCheckout()
    {
        BasketIs(StoredBasket(items: 0));
        LastCheckoutWas(Guid.NewGuid());

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketEmpty),
            "only a basket that is gone makes a request a repeat; an existing empty basket is not one");
        VerifyNothingCommitted();
    }

    [Test]
    public async Task ARefusedCommit_WithTheBasketStillThere_IsAConflict()
    {
        BasketIs(StoredBasket());
        LastCheckoutWas(Guid.NewGuid());
        CommitIsRefused();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.CheckoutConflict));
        _metrics.Verify(x => x.RecordCheckout("conflict"), Times.Once);
        _metrics.Verify(x => x.RecordCheckout("success"), Times.Never);
    }

    [Test]
    public async Task ARefusedCommit_BecauseAnotherCheckoutOfTheBasketCompleted_ReturnsThatCheckout()
    {
        var concurrentCheckout = Guid.NewGuid();
        _repository
            .SetupSequence(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredBasket())
            .ReturnsAsync((ShoppingBasket?)null);
        LastCheckoutWas(concurrentCheckout);
        CommitIsRefused();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(concurrentCheckout));
    }

    [Test]
    public async Task WhileAnotherCheckoutHoldsTheLock_TheRequestIsInProgress_AndReadsNothing()
    {
        var repository = new Mock<IBasketRepository>(MockBehavior.Strict);
        _store
            .Setup(x => x.TryBeginProcessingAsync("user-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = new CheckoutBasketCommandHandler(
            repository.Object,
            _store.Object,
            new Mock<IProductCatalogReader>(MockBehavior.Strict).Object,
            Mock.Of<ICurrentUserContext>(),
            Mock.Of<ILogger<CheckoutBasketCommandHandler>>(),
            _metrics.Object);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.CheckoutAlreadyInProgress));
        _store.Verify(x => x.ReleaseProcessingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "a lock this request did not take is not its to release");
        _metrics.Verify(x => x.RecordCheckout("in_progress"), Times.Once);
    }

    [Test]
    public async Task ACommitThatThrows_IsOperationFailed_AndReleasesTheLock()
    {
        BasketIs(StoredBasket());
        _store
            .Setup(x => x.CommitAsync(It.IsAny<ShoppingBasket>(), It.IsAny<BasketCheckedOutEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
        _store.Verify(x => x.ReleaseProcessingAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ACancelledRequest_RethrowsOperationCanceledException()
    {
        _repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        _store
            .Setup(x => x.ReleaseProcessingAsync("user-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => _handler.Handle(Command(), cts.Token));
        _metrics.Verify(x => x.RecordCheckout("failure"), Times.Never);
    }

    [Test]
    public async Task AFailedLockRelease_DoesNotThrowFromFinally()
    {
        _repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("repository failure"));
        _store
            .Setup(x => x.ReleaseProcessingAsync("user-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("release failure"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
    }

    private void CatalogReturns(decimal price, int stock)
        => _catalog
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new ProductCatalogSnapshot(id, "Product", price, stock));

    /// <summary>Basket audit S6 (H5, D6): refused, and the basket takes Catalog's price for the next attempt.</summary>
    [Test]
    public async Task ARepricedLine_RefusesTheCheckout_AndTheBasketTakesCatalogsPrice()
    {
        var basket = StoredBasket();
        BasketIs(basket);
        CatalogReturns(price: 12m, stock: 100);
        _repository
            .Setup(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        var error = result.Error as CheckoutRevalidationError;
        Assert.That(error, Is.Not.Null, "a repriced line must refuse the checkout");
        Assert.That(error!.Lines.Single().Reason, Is.EqualTo(CheckoutLineProblem.Repriced));
        Assert.That(error.Lines.Single().BasketPrice, Is.EqualTo(10m));
        Assert.That(error.Lines.Single().CatalogPrice, Is.EqualTo(12m));
        Assert.That(basket.Items.Single().Price, Is.EqualTo(12m));
        _repository.Verify(x => x.TrySaveBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNothingCommitted();
    }

    [Test]
    public async Task AnUnavailableLine_RefusesTheCheckout_WithoutRewritingTheBasket()
    {
        BasketIs(StoredBasket());
        _catalog
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCatalogSnapshot?)null);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        var line = (result.Error as CheckoutRevalidationError)!.Lines.Single();
        Assert.That(line.Reason, Is.EqualTo(CheckoutLineProblem.Unavailable));
        Assert.That(line.CatalogPrice, Is.Null);
        _repository.Verify(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingCommitted();
    }

    [Test]
    public async Task ALineBeyondTheStock_RefusesTheCheckoutAsOutOfStock()
    {
        BasketIs(StoredBasket());
        CatalogReturns(price: 10m, stock: 0);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        var line = (result.Error as CheckoutRevalidationError)!.Lines.Single();
        Assert.That(line.Reason, Is.EqualTo(CheckoutLineProblem.OutOfStock));
        Assert.That(line.AvailableQuantity, Is.EqualTo(0));
        VerifyNothingCommitted();
    }

    [Test]
    public async Task WhenCatalogCannotBeReached_ItIsProductVerificationFailed_AndNothingIsCommitted()
    {
        BasketIs(StoredBasket());
        _catalog
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("catalog down"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.ProductVerificationFailed));
        VerifyNothingCommitted();
        _store.Verify(x => x.ReleaseProcessingAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
