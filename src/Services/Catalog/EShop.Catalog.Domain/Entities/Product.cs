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
    /// Changes the descriptive fields: name, description and SKU (Admin panel S2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until this existed <c>UpdateProductCommand</c> carried exactly three fields — id, price and
    /// stock — so no endpoint anywhere could change a product's name, description or SKU after
    /// creation. An admin "edit product" form could not edit the product.
    /// </para>
    /// <para>
    /// <paramref name="description"/> follows the BUG-09 three-case rule: <c>null</c> leaves the
    /// stored value alone, blank clears it, anything else replaces it. The null check below is the
    /// contract, not a redundant guard — assigning unconditionally is exactly the bug that let a
    /// name-only update silently wipe a description.
    /// </para>
    /// <para>
    /// <b>SKU uniqueness is deliberately not checked here.</b> The aggregate cannot see other
    /// products; uniqueness is owned by the partial unique index
    /// <c>IX_Products_Sku ... WHERE NOT "IsDeleted"</c>, with a handler pre-check for the common
    /// case and <c>AddProductSkuConflict()</c> mapping a lost race. Same division of labour as
    /// <see cref="Create"/>.
    /// </para>
    /// </remarks>
    public void UpdateDetails(string name, string? description, string sku)
    {
        if (IsDeleted)
            throw new DomainException("Cannot update a deleted product.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Product name is required.");

        if (string.IsNullOrWhiteSpace(sku))
            throw new DomainException("SKU is required.");

        Name = name.Trim();
        Sku = sku.Trim();

        if (description is not null)
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    /// <summary>
    /// Moves the product to another category (Admin panel S2). Idempotent: moving to the category
    /// it is already in is a no-op.
    /// </summary>
    /// <remarks>
    /// Only the id is set. Whether the target category <i>exists</i> is a cross-aggregate question
    /// the handler answers with a repository lookup before calling this — the alternative, taking a
    /// <c>Category</c> here, would let a caller attach an unsaved category to a tracked product and
    /// have EF insert it, the trap <c>Category.ResolveRequestedSlug</c> exists to avoid.
    /// </remarks>
    public void ChangeCategory(Guid categoryId)
    {
        if (IsDeleted)
            throw new DomainException("Cannot change the category of a deleted product.");

        if (categoryId == Guid.Empty)
            throw new DomainException("Category id is required.");

        if (categoryId == CategoryId)
            return;

        CategoryId = categoryId;

        // The navigation is left alone on purpose. It may be loaded and would then contradict the
        // id; EF fixes it up on the next load, and nothing in the aggregate reads it.
        Category = null!;
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
    /// Moves stock by a relative amount (Admin panel S4) — a delivery of +50, a write-off of −3.
    /// Returns the new quantity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="UpdateStock"/> because the two answer different questions, and
    /// conflating them is how stock goes wrong. "Set it to 50" is a stock-take: the caller has
    /// counted the shelf and its answer is correct whatever the database says. "Add 50" is a
    /// movement: it composes with whatever anyone else did meanwhile. An admin UI that only has
    /// the absolute form makes every delivery a read-modify-write across an HTTP round trip, and
    /// two admins receiving two deliveries silently lose one of them.
    /// </para>
    /// <para>
    /// A delta of zero is refused rather than treated as a no-op: it always means the caller
    /// computed it, and answering 204 to a movement that moved nothing hides that.
    /// </para>
    /// </remarks>
    public int AdjustStock(int delta)
    {
        if (IsDeleted)
            throw new DomainException("Cannot adjust stock of a deleted product.");

        if (delta == 0)
            throw new DomainException("Stock adjustment cannot be zero.");

        var adjusted = (long)StockQuantity + delta;

        // Checked as a long first: StockQuantity + delta can overflow int, and an overflowed
        // negative would pass a plain `< 0` check on the wrapped value in an unchecked context.
        if (adjusted < 0)
            throw new DomainException($"Stock cannot go negative: {StockQuantity} adjusted by {delta}.");

        if (adjusted > int.MaxValue)
            throw new DomainException("Stock adjustment would exceed the maximum stock quantity.");

        StockQuantity = (int)adjusted;
        return StockQuantity;
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

    /// <summary>
    /// Brings a soft-deleted product back (Admin panel S4). Idempotent: restoring a live product
    /// does nothing, mirroring <see cref="SoftDelete"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It restores to <see cref="ProductStatus.Draft"/>, not to whatever the product was before
    /// deletion.</b> That status is not recoverable — <see cref="SoftDelete"/> overwrites it with
    /// <c>Discontinued</c> and keeps no record of the previous value — so the only honest choices
    /// are Draft or Active, and Draft is the safe one: restoring must never silently put a product
    /// back in front of customers. An admin re-publishes deliberately, through
    /// <see cref="Publish"/>. Restoring to <c>Discontinued</c> was rejected because it is
    /// indistinguishable from "still deleted" to every read path that filters on Status.
    /// </para>
    /// <para>
    /// <b>SKU uniqueness is not checked here, and cannot be.</b> <c>IX_Products_Sku</c> is unique
    /// filtered <c>NOT "IsDeleted"</c>, so a deleted product's SKU is free for another product to
    /// take — and once taken, restoring re-enters the filtered index and collides. The aggregate
    /// cannot see other products, so the pre-check lives in
    /// <c>RestoreProductCommandHandler</c> with the index as the race backstop, exactly as SKU
    /// uniqueness works on create and update.
    /// </para>
    /// </remarks>
    public void Restore()
    {
        if (!IsDeleted)
            return;

        IsDeleted = false;
        DeletedAt = null;
        Status = ProductStatus.Draft;
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

    /// <summary>
    /// Replaces an existing image's URL and alt text (Admin panel S3). Until this existed the only
    /// way to correct a mistyped CDN link was to delete the image and add it again, which loses its
    /// position and its main flag.
    /// </summary>
    /// <remarks>
    /// The duplicate-URL check excludes the image being edited, so re-submitting an unchanged URL —
    /// what an admin form does every time it saves — is allowed. Compare
    /// <c>UpdateProductCommandHandler</c>'s SKU self-exclusion, which solves the same problem one
    /// layer up.
    /// </remarks>
    public void UpdateImage(Guid imageId, string url, string? altText)
    {
        if (IsDeleted)
            throw new DomainException("Cannot update an image on a deleted product.");

        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image is null)
            throw new DomainException("Product image not found.");

        // Normalize through a throwaway instance before comparing, so the duplicate check sees the
        // same trimmed value that would be stored — the shape AddImage uses.
        var normalized = new ProductImage(Id, url, altText, image.DisplayOrder);

        if (_images.Any(x => x.Id != imageId
                && string.Equals(x.Url, normalized.Url, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("Product image URL already exists for this product.");
        }

        image.Update(url, altText);
    }

    /// <summary>
    /// Assigns <c>DisplayOrder</c> from the position of each id in <paramref name="orderedImageIds"/>
    /// (Admin panel S3). The list must be exactly this product's images — no missing, extra or
    /// duplicated ids.
    /// </summary>
    /// <remarks>
    /// Requiring the complete set rather than accepting a partial one is deliberate. A partial
    /// reorder has no single correct answer for where the omitted images land, and silently
    /// appending them is the kind of choice that only shows up as a shuffled gallery in production.
    /// A drag-and-drop admin UI holds the whole list anyway, so it costs the caller nothing.
    /// The main flag is untouched — position and "is the main image" are independent here, and
    /// <see cref="RemoveImage"/> is the only thing that elects a main by order.
    /// </remarks>
    public void ReorderImages(IReadOnlyList<Guid> orderedImageIds)
    {
        if (IsDeleted)
            throw new DomainException("Cannot reorder images on a deleted product.");

        ArgumentNullException.ThrowIfNull(orderedImageIds);

        if (orderedImageIds.Count != _images.Count)
        {
            throw new DomainException(
                $"Reordering requires every image exactly once: the product has {_images.Count} image(s) but {orderedImageIds.Count} id(s) were supplied.");
        }

        if (orderedImageIds.Distinct().Count() != orderedImageIds.Count)
            throw new DomainException("Reordering requires every image exactly once: duplicate ids were supplied.");

        // Counts match and the ids are distinct, so "every supplied id belongs to this product"
        // is enough to also prove no image was left out. Note this is deliberately `Any` rather
        // than `FirstOrDefault` — the latter returns Guid.Empty for "no match", which is
        // indistinguishable from a caller that actually supplied Guid.Empty, and that case would
        // then fall through to `Single` below and throw InvalidOperationException (a 500) instead
        // of DomainException (a 400).
        if (orderedImageIds.Any(id => _images.All(i => i.Id != id)))
            throw new DomainException("Product image not found.");

        for (var position = 0; position < orderedImageIds.Count; position++)
        {
            _images.Single(i => i.Id == orderedImageIds[position]).SetDisplayOrder(position);
        }
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

    /// <summary>
    /// Replaces an attribute's name and value (Admin panel S3).
    /// </summary>
    /// <remarks>
    /// The name-collision check excludes the attribute being edited, so saving an admin form that
    /// changed only the value is allowed. As with <see cref="AddAttribute"/>, comparison is
    /// case-insensitive, which is why the M1 index is on <c>lower("Name")</c> rather than on
    /// <c>"Name"</c> — a case-sensitive index would let "Color" and "color" both land under a race
    /// that this check would have refused.
    /// </remarks>
    public void UpdateAttribute(Guid attributeId, string name, string value)
    {
        if (IsDeleted)
            throw new DomainException("Cannot update an attribute on a deleted product.");

        var attribute = _attributes.FirstOrDefault(a => a.Id == attributeId);
        if (attribute is null)
            throw new DomainException("Product attribute not found.");

        var normalizedName = new ProductAttribute(Id, name, value).Name;

        if (_attributes.Any(a => a.Id != attributeId
                && string.Equals(a.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Attribute '{normalizedName}' already exists for this product.");
        }

        attribute.Update(name, value);
    }

    /// <summary>
    /// Removes an attribute (Admin panel S3). Throws when it does not exist, so the handler must
    /// pre-check and return a <c>*.NotFound</c> Result if the endpoint owes a 404 — the same shape
    /// as <see cref="RemoveImage"/>.
    /// </summary>
    public void RemoveAttribute(Guid attributeId)
    {
        if (IsDeleted)
            throw new DomainException("Cannot remove an attribute from a deleted product.");

        var attribute = _attributes.FirstOrDefault(a => a.Id == attributeId);
        if (attribute is null)
            throw new DomainException("Product attribute not found.");

        _attributes.Remove(attribute);
    }

    /// <summary>
    /// Sets the product's attributes to exactly <paramref name="attributes"/> (Admin panel S3),
    /// which is what an admin "edit attributes" grid submits.
    /// </summary>
    /// <remarks>
    /// <b>This reconciles by name; it does not clear and re-add.</b> Rows whose name is already
    /// present are updated in place, absent ones are removed, and only genuinely new names are
    /// inserted. The obvious implementation — empty the collection and add everything back — has
    /// two problems. Every attribute would get a fresh Guid on every save, so any future reference
    /// to an attribute id would break for no reason. Worse, it makes a DELETE and an INSERT of the
    /// <i>same</i> <c>(ProductId, lower(Name))</c> key land in one <c>SaveChanges</c>, and EF Core
    /// does not guarantee it emits the DELETE first — against M1's non-deferrable unique index,
    /// Postgres would then abort the whole batch with 23505 for a save that changed nothing but a
    /// value. That is the same hazard <see cref="ClearMainImage"/> documents for the IsMain index,
    /// and reconciling sidesteps it entirely rather than working around it.
    ///
    /// <para>
    /// A name that differs only in case updates the existing row rather than adding a second one,
    /// matching <see cref="AddAttribute"/>'s case-insensitive dedupe. The stored name becomes the
    /// submitted casing.
    /// </para>
    /// </remarks>
    public void ReplaceAttributes(IReadOnlyList<(string Name, string Value)> attributes)
    {
        if (IsDeleted)
            throw new DomainException("Cannot replace attributes on a deleted product.");

        ArgumentNullException.ThrowIfNull(attributes);

        if (attributes.Count > MaxAttributes)
            throw new DomainException($"A product cannot have more than {MaxAttributes} attributes.");

        // Normalize first, so the cap, the duplicate check and the reconciliation below all see the
        // trimmed names that would actually be stored. Constructing validates each pair the same
        // way AddAttribute does; these instances are unreachable from the aggregate, so EF never
        // discovers them — only the ones added to _attributes below.
        var incoming = attributes
            .Select(a => new ProductAttribute(Id, a.Name, a.Value))
            .ToList();

        var duplicateName = incoming
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateName is not null)
            throw new DomainException($"Attribute '{duplicateName.Key}' is listed more than once.");

        var incomingNames = incoming.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var removed in _attributes.Where(a => !incomingNames.Contains(a.Name)).ToList())
        {
            _attributes.Remove(removed);
        }

        foreach (var candidate in incoming)
        {
            var existing = _attributes.FirstOrDefault(
                a => string.Equals(a.Name, candidate.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _attributes.Add(candidate);
            }
            else
            {
                existing.Update(candidate.Name, candidate.Value);
            }
        }
    }
}

public enum ProductStatus
{
    Draft,
    Active,
    Discontinued
}
