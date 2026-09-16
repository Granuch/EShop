using EShop.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace EShop.Catalog.Infrastructure.Data;

/// <summary>
/// Seeds a small, realistic catalog (categories + products with images/attributes/discounts)
/// for local development, manual QA and demos.
///
/// Idempotent: skipped once any category already exists, so it is safe to call on every
/// startup (container restarts, redeploys) without producing duplicates.
///
/// Deliberately goes through the same path as CreateProductCommandHandler
/// (Product.Create / AddImage / AddAttribute / Publish -> DbSet.AddRangeAsync -> SaveChangesAsync)
/// rather than raw SQL or EF `HasData` migration seeding. CatalogDbContext (via BaseDbContext)
/// dispatches domain events -> outbox -> integration events *inside* SaveChangesAsync, so this
/// is the only approach that also notifies downstream consumers (e.g. Basket's catalog read
/// model) about the seeded products. Bypassing SaveChangesAsync would leave them invisible
/// outside Catalog itself.
/// </summary>
public static class CatalogSeedData
{
    public static async Task SeedAsync(
        CatalogDbContext context,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (await context.Categories.AnyAsync(cancellationToken))
        {
            logger?.Information("Catalog already has data, skipping seed");
            return;
        }

        var electronics = Category.Create("Electronics", "electronics", parent: null,
            description: "Gadgets and devices");
        var phones = Category.Create("Phones", "phones", parent: electronics);
        var laptops = Category.Create("Laptops", "laptops", parent: electronics);
        var books = Category.Create("Books", "books", parent: null);

        var products = new List<Product>();

        // Published product with 2 images, several attributes and an active discount -
        // exercises MainImageUrl ordering, Images[]/Attributes[] projection and EffectivePrice.
        var phone1 = Product.Create("Phantom X12", "PHN-X12-001", 899.99m, 50, phones.Id,
            "Flagship smartphone with an OLED display");
        phone1.AddImage("https://picsum.photos/seed/phn-x12-1/800/800", "Phantom X12 front", 0);
        phone1.AddImage("https://picsum.photos/seed/phn-x12-2/800/800", "Phantom X12 back", 1);
        phone1.AddAttribute("Color", "Midnight Black");
        phone1.AddAttribute("Storage", "256GB");
        phone1.AddAttribute("RAM", "12GB");
        phone1.SetDiscountPrice(799.99m);
        phone1.Publish();
        products.Add(phone1);

        // Published, single image, no discount - the "plain" case.
        var phone2 = Product.Create("Phantom X12 Lite", "PHN-X12L-001", 549.99m, 120, phones.Id);
        phone2.AddImage("https://picsum.photos/seed/phn-x12l/800/800", "Phantom X12 Lite", 0);
        phone2.AddAttribute("Color", "Silver");
        phone2.Publish();
        products.Add(phone2);

        // Left in Draft on purpose - for testing that unpublished products are hidden from
        // public listing endpoints but visible to admin ones.
        var draftPhone = Product.Create("Phantom X13 (prerelease)", "PHN-X13-000", 999.99m, 0, phones.Id);
        products.Add(draftPhone);

        var laptop1 = Product.Create("NovaBook Pro 14", "NB-PRO14-001", 1299.00m, 30, laptops.Id,
            "14-inch ultrabook");
        laptop1.AddImage("https://picsum.photos/seed/nb-pro14/800/800", "NovaBook Pro 14", 0);
        laptop1.AddAttribute("CPU", "8-core");
        laptop1.AddAttribute("RAM", "16GB");
        laptop1.AddAttribute("Storage", "512GB SSD");
        laptop1.Publish();
        products.Add(laptop1);

        // A product with no images at all - for testing the MainImageUrl = null case.
        var book1 = Product.Create("Domain-Driven Design", "BOOK-DDD-001", 42.00m, 200, books.Id);
        book1.AddAttribute("Author", "Eric Evans");
        book1.AddAttribute("Pages", "560");
        book1.Publish();
        products.Add(book1);

        await context.Categories.AddRangeAsync(
            new[] { electronics, phones, laptops, books }, cancellationToken);
        await context.Products.AddRangeAsync(products, cancellationToken);

        // Domain events raised inside Product.Create/Publish (ProductCreatedEvent etc.) are
        // dispatched to the outbox here, as part of this same SaveChangesAsync call.
        await context.SaveChangesAsync(cancellationToken);

        logger?.Information(
            "Seeded {CategoryCount} categories and {ProductCount} products",
            4, products.Count);
    }
}