using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Domain.Events;

namespace EShop.Catalog.Domain.Entities;

/// <summary>
/// Product aggregate root
/// </summary>
public class Product : AggregateRoot<Guid>
{
    private const int MaxImages = 10;
    private const int MaxAttributes = 50;

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public decimal? DiscountPrice { get; private set; }
    public int StockQuantity { get; private set; }
    public ProductStatus Status { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;

    private readonly List<ProductImage> _images = new();
    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();

    private readonly List<ProductAttribute> _attributes = new();
    public IReadOnlyCollection<ProductAttribute> Attributes => _attributes.AsReadOnly();

    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    /// What a customer actually pays: <see cref="DiscountPrice"/> when one is active, otherwise
    /// <see cref="Price"/>.
    ///
    /// <para>
    /// This existed already, but only in Basket, as an inline
    /// <c>payload.DiscountPrice ?? payload.Price</c> in <c>CatalogProductCatalogReader</c> — a
    /// pricing rule owned by the consuming context rather than by the aggregate that owns the
    /// prices. Naming it here is what lets <see cref="ProductPriceChangedEvent"/> carry the price
    /// the customer sees rather than the list price; see <see cref="UpdatePrice"/>.
    /// </para>
    /// </summary>
    public decimal EffectivePrice => DiscountPrice ?? Price;

    private Product() { }
    
    /// <param name="description">
    /// Optional. Trimmed; blank or whitespace-only input is stored as null so "absent" and
    /// "empty string" are indistinguishable downstream. Optional with a default so the many
    /// existing five-argument call sites keep compiling.
    /// </param>
    public static Product Create(string name, string sku, decimal price, int stockQuantity, Guid categoryId, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Product name is required.");

        if (string.IsNullOrWhiteSpace(sku))
            throw new DomainException("SKU is required.");

        if (price <= 0)
            throw new DomainException("Price must be greater than zero.");

        if (stockQuantity < 0)
            throw new DomainException("Stock quantity cannot be negative.");
        
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Sku = sku,
            Price = price,
            StockQuantity = stockQuantity,
            Status = ProductStatus.Draft
        };

        product.AddDomainEvent(new ProductCreatedEvent
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Price = product.Price,
        });
        
        return product;
    }
    
    /// <summary>
    /// Changes the list price.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rejects a price at or below an active discount rather than silently dropping the discount.
    /// Both alternatives are worse: leaving it would make <see cref="EffectivePrice"/> exceed
    /// <see cref="Price"/> — Basket charging more than the catalog displays — and clearing it
    /// automatically is the "PUT silently wipes a field the caller never mentioned" shape this repo
    /// already carries as BUG-09. An admin lowering a price past a promotion clears the promotion
    /// first, explicitly.
    /// </para>
    /// <para>
    /// The event carries <see cref="EffectivePrice"/>, not <see cref="Price"/>, and is raised only
    /// when that value moves. Basket's <c>ProductPriceChangedConsumer</c> writes
    /// <c>NewPrice</c> straight onto a basket item whose price came from the effective price at
    /// add time, so publishing the list price here would reprice a discounted item up to full
    /// price. A consequence worth knowing: while a discount is active a list-price change moves
    /// nothing customer-facing (the discount is always strictly lower, so it still wins) and
    /// therefore raises no event at all.
    /// </para>
    /// </remarks>
    public void UpdatePrice(decimal newPrice)
    {
        // L21. Every other mutator refuses a deleted product; this one did not. Unreachable through
        // the API today (the !IsDeleted query filter hides deleted products from every load), which
        // is exactly why the invariant belongs here rather than relying on that.
        if (IsDeleted)
        {
            throw new DomainException("Cannot update the price of a deleted product.");
        }

        if (newPrice <= 0)
        {
            throw new DomainException("Price must be greater than zero.");
        }

        if (DiscountPrice.HasValue && newPrice <= DiscountPrice.Value)
        {
            throw new DomainException(
                "Price must be greater than the active discount price. Clear the discount first.");
        }

        if (newPrice == Price)
        {
            return;
        }

        var oldEffectivePrice = EffectivePrice;

        Price = newPrice;

        RaisePriceChangedIfEffectivePriceMoved(oldEffectivePrice);
    }

    /// <summary>
    /// Sets a promotional price below the list price (H5b / D3).
    /// </summary>
    /// <remarks>
    /// Until this existed <see cref="DiscountPrice"/> had no mutator at all, so it was permanently
    /// <c>null</c> while still being projected into both DTOs and consumed by Basket — a field
    /// another bounded context priced against and Catalog could not populate.
    /// </remarks>
    public void SetDiscountPrice(decimal discountPrice)
    {
        if (IsDeleted)
            throw new DomainException("Cannot set the discount price of a deleted product.");

        if (discountPrice <= 0)
            throw new DomainException("Discount price must be greater than zero.");

        // Strictly below, not "at most": a discount equal to the price is not a discount, and
        // allowing it would let UpdatePrice's guard reject a no-op price change.
        if (discountPrice >= Price)
            throw new DomainException("Discount price must be less than the product price.");

        var oldEffectivePrice = EffectivePrice;

        DiscountPrice = discountPrice;

        RaisePriceChangedIfEffectivePriceMoved(oldEffectivePrice);
    }

    /// <summary>
    /// Ends a promotion, returning the customer-facing price to <see cref="Price"/>. Idempotent:
    /// clearing an absent discount is a no-op, so a retried request does not fail.
    /// </summary>
    public void ClearDiscountPrice()
    {
        if (IsDeleted)
            throw new DomainException("Cannot clear the discount price of a deleted product.");

        if (DiscountPrice is null)
            return;

        var oldEffectivePrice = EffectivePrice;

        DiscountPrice = null;

        RaisePriceChangedIfEffectivePriceMoved(oldEffectivePrice);
    }

    /// <summary>
    /// Raises <see cref="ProductPriceChangedEvent"/> when the customer-facing price has moved.
    /// Call after mutating <see cref="Price"/> or <see cref="DiscountPrice"/>, never before.
    /// </summary>
    private void RaisePriceChangedIfEffectivePriceMoved(decimal oldEffectivePrice)
    {
        if (EffectivePrice == oldEffectivePrice)
            return;

        AddDomainEvent(new ProductPriceChangedEvent
        {
            ProductId = Id,
            OldPrice = oldEffectivePrice,
            NewPrice = EffectivePrice
        });
    }


    /// <summary>
    /// Sets the stock level. Deliberately raises no event. The out-of-stock / back-in-stock domain
    /// events this used to raise had no handler in Catalog and no consumer anywhere — nothing outside
    /// Catalog reads stock — so each transition wrote an outbox row that was dispatched to nobody.
    /// They were deleted in Catalog audit Stage 7 (M6) rather than wired to integration events,
    /// because an integration event with no consumer is the same dead weight D6 removed. A domain
    /// event is cheap to re-add once something actually needs to hear about stock.
    /// </summary>
    public void UpdateStock(int quantity)
    {
        if (IsDeleted)
            throw new DomainException("Cannot update stock of a deleted product.");

        if (quantity < 0)
            throw new DomainException("Stock quantity cannot be negative.");

        StockQuantity = quantity;
    }
    
    /// <summary>
    /// Makes the product publicly visible. Until Stage 4 this had no production caller, so every
    /// product was permanently <see cref="ProductStatus.Draft"/> and the public catalog served
    /// nothing but drafts — the enum existed and was unit-tested, which is what made it look done.
    /// </summary>
    public void Publish()
    {
        if (IsDeleted)
            throw new DomainException("Cannot publish a deleted product.");

        if (Status != ProductStatus.Draft)
            throw new DomainException("Cannot publish a non-draft product.");

        Status = ProductStatus.Active;
    }

    /// <summary>
    /// Withdraws a published product from the public catalog, returning it to
    /// <see cref="ProductStatus.Draft"/>.
    ///
    /// <para>
    /// Deliberately not reachable from <see cref="ProductStatus.Discontinued"/>: that state is set
    /// only by <see cref="SoftDelete"/>, and a soft-deleted product is already hidden by the
    /// <c>!p.IsDeleted</c> global query filter. Allowing Discontinued → Draft would resurrect a
    /// deleted product through a side door.
    /// </para>
    /// </summary>
    public void Unpublish()
    {
        if (IsDeleted)
            throw new DomainException("Cannot unpublish a deleted product.");

        if (Status != ProductStatus.Active)
            throw new DomainException("Cannot unpublish a product that is not active.");

        Status = ProductStatus.Draft;
    }

    public void SoftDelete()
    {
        if (IsDeleted)
            return;
        
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        Status = ProductStatus.Discontinued;
    }
    
    public Guid AddImage(string url, string? altText, int displayOrder)
    {
        if (IsDeleted)
            throw new DomainException("Cannot add image to a deleted product.");

        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("Product image URL cannot be empty.");

        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        if (_images.Count >= MaxImages)
            throw new DomainException($"A product cannot have more than {MaxImages} images.");

        // Constructing normalizes and validates the URL, so the duplicate check below
        // compares normalized values rather than raw input.
        var newImage = new ProductImage(Id, url, altText, displayOrder);

        if (_images.Any(x => string.Equals(x.Url, newImage.Url, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("Product image URL already exists for this product.");

        if (_images.Count == 0)
        {
            newImage.SetAsMain();
        }

        _images.Add(newImage);

        return newImage.Id;
    }

    public void SetMainImage(Guid imageId)
    {
        if (IsDeleted)
            throw new DomainException("Cannot update main image for a deleted product.");

        var targetImage = _images.FirstOrDefault(i => i.Id == imageId);
        if (targetImage is null)
            throw new DomainException("Product image not found.");

        foreach (var image in _images)
        {
            image.UnsetAsMain();
        }

        targetImage.SetAsMain();
    }

    /// <summary>
    /// Clears the current main image without electing a replacement, leaving the product
    /// with zero mains (which the DB permits). Returns true when an image was actually
    /// demoted.
    /// </summary>
    /// <remarks>
    /// Exists so the application layer can persist the demotion *before* the promotion when
    /// the main flag moves between two surviving rows. "At most one main" is backed by a
    /// <b>non-deferrable</b> partial unique index (<c>ProductImages (ProductId) WHERE IsMain</c>),
    /// and EF Core does not guarantee it emits the UNSET UPDATE before the SET one — when the
    /// SET lands first, Postgres sees two IsMain rows mid-batch and aborts the whole command
    /// with 23505. Splitting the two writes across separate SaveChanges calls is what keeps
    /// that transient state from ever reaching the database. <see cref="RemoveImage"/> does not
    /// need this: it deletes the outgoing main in the same batch, so no second IsMain row
    /// survives to collide with the promotion.
    /// </remarks>
    public bool ClearMainImage()
    {
        if (IsDeleted)
            throw new DomainException("Cannot update main image for a deleted product.");

        var currentMain = _images.FirstOrDefault(i => i.IsMain);
        if (currentMain is null)
            return false;

        currentMain.UnsetAsMain();
        return true;
    }

    public void RemoveImage(Guid imageId)
    {
        if (IsDeleted)
            throw new DomainException("Cannot remove image from a deleted product.");

        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image == null)
            throw new DomainException("Product image not found.");

        _images.Remove(image);

        if (image.IsMain && _images.Count > 0)
        {
            var nextMainImage = _images
                .OrderBy(i => i.DisplayOrder)
                .ThenBy(i => i.CreatedAt)
                .First();

            SetMainImage(nextMainImage.Id);
        }
    }
    
    public Guid AddAttribute(string name, string value)
    {
        if (IsDeleted)
            throw new DomainException("Cannot add attribute to a deleted product.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Attribute name cannot be empty.");

        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Attribute value cannot be empty.");

        if (_attributes.Count >= MaxAttributes)
            throw new DomainException($"A product cannot have more than {MaxAttributes} attributes.");

        // Constructing normalizes (trims) the name, so the duplicate check below compares
        // normalized values rather than raw input — same shape as AddImage.
        var newAttribute = new ProductAttribute(Id, name, value);

        if (_attributes.Any(x => string.Equals(x.Name, newAttribute.Name, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"Attribute '{newAttribute.Name}' already exists for this product.");

        _attributes.Add(newAttribute);

        return newAttribute.Id;
    }
}

public enum ProductStatus
{
    Draft,
    Active,
    Discontinued
}
