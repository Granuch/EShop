using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Basket.Domain.Events;

namespace EShop.Basket.Domain.Entities;

/// <summary>
/// Shopping basket aggregate root
/// </summary>
public class ShoppingBasket : AggregateRoot<string>
{
    /// <summary>
    /// Basket audit S9 (M2, D9): the most of one product a line can hold. Checked on every change, never on a read, so a
    /// document stored before the limit existed still loads — and can be brought under it by lowering its quantity.
    /// </summary>
    public const int MaxQuantityPerLine = 999;

    /// <summary>Basket audit S9 (M2, D9): the most different products one basket can hold.</summary>
    public const int MaxLines = 100;

    /// <summary>
    /// The currency of every price in a basket (Basket audit S11, debt 7). Catalog prices in USD, and Ordering and Payment
    /// refuse any other currency; the checkout event now says so instead of leaving it implied.
    /// </summary>
    public const string Currency = "USD";

    public string UserId { get; private set; } = string.Empty;

    private readonly List<BasketItem> _items = new();
    public IReadOnlyCollection<BasketItem> Items => _items.AsReadOnly();

    /// <summary>
    /// When the basket last changed: the base class's <see cref="Entity{TId}.UpdatedAt"/>, or its creation time if it has
    /// not changed since. Basket audit L1 (S9): this used to be a second field, kept separately from <c>UpdatedAt</c>.
    /// </summary>
    public DateTime LastModifiedAt => UpdatedAt ?? CreatedAt;

    /// <summary>
    /// True once <see cref="Checkout"/> has run. A checked-out basket takes no further change and cannot be checked out
    /// again (Basket audit L1, S9) — it used to raise a second checkout event, and only the handler prevented that.
    /// </summary>
    public bool IsCheckedOut { get; private set; }

    /// <summary>
    /// The stored state this basket was read from, opaque to the domain; <c>null</c> for a basket that was never
    /// persisted. A write conditioned on it succeeds only if nothing has changed the stored basket since the read —
    /// checkout's atomic commit is (Basket audit S3).
    /// </summary>
    public string? ConcurrencyToken { get; private set; }

    /// <summary>
    /// Records the stored state this basket now matches, after a successful write, so a later conditional write of the
    /// same object is judged against what it itself stored. Called by the repository.
    /// </summary>
    public void MarkStored(string concurrencyToken) => ConcurrencyToken = concurrencyToken;

    public decimal TotalPrice => _items.Sum(i => i.SubTotal);

    /// <summary>
    /// Summed as <c>long</c> (Basket audit S9, M2). A document stored before the per-line limit can hold lines of up to
    /// <c>int.MaxValue</c>; two of them overflowed LINQ's checked <c>int</c> sum, and that basket's GET failed for good.
    /// </summary>
    public long TotalItems => _items.Sum(i => (long)i.Quantity);

    private ShoppingBasket() { }

    public static ShoppingBasket Create(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("User ID is required.");

        return new ShoppingBasket
        {
            Id = userId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static ShoppingBasket Rehydrate(
        string userId,
        DateTime createdAt,
        DateTime lastModifiedAt,
        IReadOnlyCollection<StoredBasketItem> items,
        string? concurrencyToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("User ID is required.");

        var basket = new ShoppingBasket
        {
            Id = userId,
            UserId = userId,
            CreatedAt = createdAt,
            UpdatedAt = lastModifiedAt,
            ConcurrencyToken = concurrencyToken
        };

        foreach (var item in items)
        {
            basket._items.Add(BasketItem.Rehydrate(item, addedAtIfUnknown: createdAt));
        }

        return basket;
    }

    public void AddItem(Guid productId, string productName, decimal price, int quantity, string? mainImageUrl = null)
    {
        EnsureNotCheckedOut();

        if (productId == Guid.Empty)
            throw new DomainException("Product ID is required.");

        if (string.IsNullOrWhiteSpace(productName))
            throw new DomainException("Product name is required.");

        if (price < 0)
            throw new DomainException("Price cannot be negative.");

        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
        {
            // In long: a line stored before the limit can be near int.MaxValue, and the int sum wrapped around.
            var mergedQuantity = (long)existingItem.Quantity + quantity;
            EnsureWithinLineLimit(mergedQuantity);

            // Basket audit M1: the caller has just read Catalog, so these are the product's current name and price.
            // Keeping the stored ones meant a missed or late price event was never repaired by adding the product again.
            existingItem.Refresh(productName, price, mainImageUrl);
            existingItem.UpdateQuantity((int)mergedQuantity);
        }
        else
        {
            EnsureWithinLineLimit(quantity);

            if (_items.Count >= MaxLines)
                throw new DomainException($"A basket can hold at most {MaxLines} different products.");

            _items.Add(new BasketItem(productId, productName, price, quantity, mainImageUrl));
        }

        Touch();
    }

    public void UpdateItemQuantity(Guid productId, int newQuantity)
    {
        EnsureNotCheckedOut();

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem == null)
            throw new DomainException($"Product '{productId}' is not in basket.");

        if (newQuantity <= 0)
        {
            _items.Remove(existingItem);
        }
        else
        {
            EnsureWithinLineLimit(newQuantity);
            existingItem.UpdateQuantity(newQuantity);
        }

        Touch();
    }

    public void RemoveItem(Guid productId)
    {
        EnsureNotCheckedOut();

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem == null)
            return;

        _items.Remove(existingItem);
        Touch();
    }

    public void Clear()
    {
        EnsureNotCheckedOut();

        _items.Clear();
        Touch();
    }

    /// <returns>Whether the basket changed: false if it does not hold the product or already has that price.</returns>
    public bool ApplyPriceChange(Guid productId, decimal newPrice)
    {
        EnsureNotCheckedOut();

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem == null || existingItem.Price == newPrice)
            return false;

        existingItem.UpdatePrice(newPrice);
        Touch();
        return true;
    }

    /// <summary>
    /// Raises the checkout event. There is no payment method (Basket audit S11, D11): it was free text nothing downstream
    /// read — Payment chooses the method itself when the payment intent is created.
    /// </summary>
    public void Checkout(ValueObjects.ShippingAddress shippingAddress)
    {
        if (IsCheckedOut)
            throw new DomainException("This basket has already been checked out.");

        if (_items.Count == 0)
            throw new DomainException("Cannot checkout an empty basket.");

        if (shippingAddress is null)
            throw new DomainException("Shipping address is required.");

        AddDomainEvent(new BasketCheckedOutDomainEvent
        {
            UserId = UserId,
            Items = _items
                .Select(item => new BasketCheckedOutDomainEventItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Price = item.Price,
                    Quantity = item.Quantity
                })
                .ToList(),
            TotalPrice = TotalPrice,
            ShippingAddress = shippingAddress
        });

        IsCheckedOut = true;
        Touch();
    }

    private static void EnsureWithinLineLimit(long quantity)
    {
        if (quantity > MaxQuantityPerLine)
            throw new DomainException($"A basket line can hold at most {MaxQuantityPerLine} of a product.");
    }

    private void EnsureNotCheckedOut()
    {
        if (IsCheckedOut)
            throw new DomainException("This basket has already been checked out.");
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
