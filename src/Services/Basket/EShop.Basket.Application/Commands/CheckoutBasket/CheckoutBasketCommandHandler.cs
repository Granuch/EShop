using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Application.Telemetry;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Events;
using EShop.Basket.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using DomainShippingAddress = EShop.Basket.Domain.ValueObjects.ShippingAddress;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

/// <summary>
/// Checks a basket out (Basket audit S3: C2, H1, H2, M7; S6: H5; decisions D2, D3, D6).
///
/// <para><b>Every line is re-read from Catalog first (S6, D6).</b> A product that is gone from the public catalog
/// (deleted, unpublished, discontinued), short of stock, or repriced refuses the checkout with
/// <see cref="CheckoutRevalidationError"/>, listing each line and why. Before refusing, the basket takes Catalog's
/// current prices, so the customer sees them and checks out again at a price they have seen. Ordering trusts the
/// prices Basket sends, so this is the only place they are checked; Catalog was read once, when the item was added.</para>
///
/// <para><b>One atomic step.</b> <see cref="IBasketCheckoutStore.CommitAsync"/> queues the integration event, deletes
/// the basket and its index entries and records the completed checkout in one Redis transaction, and only if the stored
/// basket is still exactly the one read here. So a checkout either happened completely or wrote nothing: success is
/// never reported without a stored event (H1), and no failure leaves an event queued behind an error that invites a
/// second order (H2). Nothing after the commit can fail the request.</para>
///
/// <para><b>What counts as a repeat (D2).</b> Only a request that finds <i>no</i> basket while the user's completed
/// marker exists — i.e. a retry of a checkout that already went through — gets that checkout's id back. A basket that
/// exists is always checked out as new, however recently the user checked out before; the per-user marker used to
/// swallow every checkout for 15 minutes after the last one (C2).</para>
///
/// <para><b>The checkoutId (D3)</b> is the integration event's <c>EventId</c>, which the outbox publishes as the
/// MassTransit <c>MessageId</c>: the id Ordering logs and deduplicates on.</para>
/// </summary>
public class CheckoutBasketCommandHandler : IRequestHandler<CheckoutBasketCommand, Result<Guid>>
{
    private static readonly TimeSpan CheckoutProcessingTtl = TimeSpan.FromMinutes(3);

    private readonly IBasketRepository _basketRepository;
    private readonly IBasketCheckoutStore _checkoutStore;
    private readonly IProductCatalogReader _productCatalogReader;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<CheckoutBasketCommandHandler> _logger;
    private readonly IBasketMetrics _metrics;

    public CheckoutBasketCommandHandler(
        IBasketRepository basketRepository,
        IBasketCheckoutStore checkoutStore,
        IProductCatalogReader productCatalogReader,
        ICurrentUserContext currentUserContext,
        ILogger<CheckoutBasketCommandHandler> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _checkoutStore = checkoutStore;
        _productCatalogReader = productCatalogReader;
        _currentUserContext = currentUserContext;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<Guid>> Handle(CheckoutBasketCommand request, CancellationToken cancellationToken)
    {
        using var activity = BasketActivitySource.Source.StartActivity("Basket.Checkout");
        using var timer = _metrics.MeasureOperation("checkout");
        var processingLockAcquired = false;

        activity?.SetTag("basket.user_id", request.UserId);

        try
        {
            if (!await _checkoutStore.TryBeginProcessingAsync(request.UserId, CheckoutProcessingTtl, cancellationToken))
            {
                _metrics.RecordCheckout("in_progress");
                return Result<Guid>.Failure(BasketErrors.CheckoutAlreadyInProgress);
            }

            processingLockAcquired = true;

            var basket = await _basketRepository.GetBasketAsync(request.UserId, cancellationToken);
            if (basket == null)
            {
                return await AlreadyCheckedOutOrAsync(request.UserId, BasketErrors.BasketEmpty, cancellationToken);
            }

            if (basket.Items.Count == 0)
            {
                _metrics.RecordCheckout("failure");
                return Result<Guid>.Failure(BasketErrors.BasketEmpty);
            }

            IReadOnlyList<(BasketItem Item, ProductCatalogSnapshot? Product)> catalog;
            try
            {
                catalog = await ReadCatalogAsync(basket, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Not verified is not checked out: without Catalog there is no way to know what is being bought.
                _logger.LogError(ex, "Catalog revalidation failed at checkout. UserId={UserId}", request.UserId);
                _metrics.RecordCheckout("failure");
                return Result<Guid>.Failure(BasketErrors.ProductVerificationFailed);
            }

            var problems = FindProblems(catalog);
            if (problems.Count > 0)
            {
                await TakeCatalogPricesAsync(basket, catalog, cancellationToken);

                _logger.LogInformation(
                    "Checkout refused: {LineCount} line(s) no longer match the catalog. UserId={UserId}",
                    problems.Count,
                    request.UserId);
                _metrics.RecordCheckout("revalidation_failed");
                return Result<Guid>.Failure(new CheckoutRevalidationError(problems));
            }

            // Non-null: the validator requires it.
            var address = request.ShippingAddress!;
            basket.Checkout(
                DomainShippingAddress.Create(address.Street, address.City, address.State, address.ZipCode, address.Country));

            var domainEvent = basket.DomainEvents.OfType<BasketCheckedOutDomainEvent>().Single();
            var checkoutEvent = BasketCheckedOutEventMapper.ToIntegrationEvent(domainEvent, _currentUserContext.CorrelationId);

            if (!await _checkoutStore.CommitAsync(basket, checkoutEvent, cancellationToken))
            {
                // Nothing was written: the stored basket changed after it was read. If it is gone, a concurrent
                // checkout of it completed; otherwise the customer must see what they are now buying.
                var current = await _basketRepository.GetBasketAsync(request.UserId, cancellationToken);
                if (current == null)
                {
                    return await AlreadyCheckedOutOrAsync(request.UserId, BasketErrors.CheckoutConflict, cancellationToken);
                }

                _logger.LogInformation(
                    "Checkout refused: the basket changed while it was being checked out. UserId={UserId}",
                    request.UserId);
                _metrics.RecordCheckout("conflict");
                return Result<Guid>.Failure(BasketErrors.CheckoutConflict);
            }

            basket.ClearDomainEvents();

            _logger.LogInformation(
                "Basket checked out. UserId={UserId}, CheckoutId={CheckoutId}",
                request.UserId,
                checkoutEvent.EventId);

            _metrics.RecordCheckout("success");
            activity?.SetTag("basket.total_price", checkoutEvent.TotalPrice);

            return Result<Guid>.Success(checkoutEvent.EventId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordCheckout("failure");

            _logger.LogError(ex,
                "Failed to checkout basket. UserId={UserId}",
                request.UserId);

            return Result<Guid>.Failure(BasketErrors.BasketOperationFailed);
        }
        finally
        {
            if (processingLockAcquired)
            {
                try
                {
                    await _checkoutStore.ReleaseProcessingAsync(request.UserId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception releaseEx)
                {
                    _logger.LogWarning(releaseEx,
                        "Failed to release checkout processing lock. UserId={UserId}",
                        request.UserId);
                }
            }
        }
    }

    /// <summary>Every line with what the public catalog says about its product now (null: not available).</summary>
    private async Task<IReadOnlyList<(BasketItem Item, ProductCatalogSnapshot? Product)>> ReadCatalogAsync(
        ShoppingBasket basket,
        CancellationToken cancellationToken)
    {
        var reads = basket.Items.Select(async item =>
            (item, await _productCatalogReader.GetByIdAsync(item.ProductId, cancellationToken)));

        return await Task.WhenAll(reads);
    }

    /// <summary>One problem per line, the most serious first: unavailable, then out of stock, then repriced.</summary>
    private static List<CheckoutLineProblem> FindProblems(
        IReadOnlyList<(BasketItem Item, ProductCatalogSnapshot? Product)> catalog)
    {
        var problems = new List<CheckoutLineProblem>();

        foreach (var (item, product) in catalog)
        {
            var reason = product switch
            {
                null => CheckoutLineProblem.Unavailable,
                _ when product.StockQuantity < item.Quantity => CheckoutLineProblem.OutOfStock,
                _ when product.Price != item.Price => CheckoutLineProblem.Repriced,
                _ => null
            };

            if (reason != null)
            {
                problems.Add(new CheckoutLineProblem(
                    item.ProductId,
                    reason,
                    item.Quantity,
                    item.Price,
                    product?.StockQuantity,
                    product?.Price));
            }
        }

        return problems;
    }

    /// <summary>
    /// D6: the refused basket takes Catalog's current prices, so what the customer sees next is what they would be
    /// charged. Best effort — conditioned on the basket as read, and a lost race or a failure only means the next
    /// checkout revalidates again; it must not turn the refusal into a different error.
    /// </summary>
    private async Task TakeCatalogPricesAsync(
        ShoppingBasket basket,
        IReadOnlyList<(BasketItem Item, ProductCatalogSnapshot? Product)> catalog,
        CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var (item, product) in catalog)
        {
            if (product != null)
            {
                changed |= basket.ApplyPriceChange(item.ProductId, product.Price);
            }
        }

        if (!changed)
        {
            return;
        }

        try
        {
            if (!await _basketRepository.TrySaveBasketAsync(basket, cancellationToken))
            {
                _logger.LogInformation(
                    "Catalog prices not stored after a refused checkout: the basket changed meanwhile. UserId={UserId}",
                    basket.UserId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Failed to store catalog prices after a refused checkout. UserId={UserId}",
                basket.UserId);
        }
    }

    /// <summary>
    /// The basket is gone. If the user has a completed checkout, this request repeats it — return that checkout's id
    /// (D2); otherwise fail with <paramref name="otherwise"/>.
    /// </summary>
    private async Task<Result<Guid>> AlreadyCheckedOutOrAsync(string userId, Error otherwise, CancellationToken cancellationToken)
    {
        var completedCheckoutId = await _checkoutStore.GetCompletedCheckoutIdAsync(userId, cancellationToken);
        if (completedCheckoutId is { } checkoutId)
        {
            _logger.LogInformation(
                "Returning the completed checkout this request repeats. UserId={UserId}, CheckoutId={CheckoutId}",
                userId,
                checkoutId);

            _metrics.RecordCheckout("deduplicated");
            return Result<Guid>.Success(checkoutId);
        }

        _metrics.RecordCheckout(otherwise == BasketErrors.CheckoutConflict ? "conflict" : "failure");
        return Result<Guid>.Failure(otherwise);
    }
}
