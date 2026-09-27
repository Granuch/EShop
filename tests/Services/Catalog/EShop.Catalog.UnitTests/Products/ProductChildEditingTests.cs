using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.UnitTests.Products;

/// <summary>
/// The child mutators added by Admin panel S3: <c>UpdateImage</c>, <c>ReorderImages</c>,
/// <c>UpdateAttribute</c>, <c>RemoveAttribute</c> and <c>ReplaceAttributes</c>. Before these,
/// images could only be added, removed or promoted, and attributes could only be added — so a
/// mistyped attribute was permanent and the 50-attribute cap was a one-way ratchet.
/// </summary>
[TestFixture]
public class ProductChildEditingTests
{
    private static Product NewProduct()
        => Product.Create("Original", "SKU-ORIG", 10m, 5, Guid.NewGuid());

    #region UpdateImage

    [Test]
    public void UpdateImage_ReplacesUrlAndAltText()
    {
        var product = NewProduct();
        var imageId = product.AddImage("https://cdn.example.com/a.jpg", "Old alt", 0);

        product.UpdateImage(imageId, "https://cdn.example.com/b.jpg", "New alt");

        var image = product.Images.Single();
        Assert.Multiple(() =>
        {
            Assert.That(image.Url, Is.EqualTo("https://cdn.example.com/b.jpg"));
            Assert.That(image.AltText, Is.EqualTo("New alt"));
        });
    }

    [Test]
    public void UpdateImage_WithNoAltText_ClearsIt()
    {
        // Full-replacement PUT semantics, deliberately unlike Product.UpdateDetails' description,
        // which follows the three-case BUG-09 rule because its endpoint predates the field.
        var product = NewProduct();
        var imageId = product.AddImage("https://cdn.example.com/a.jpg", "Has alt", 0);

        product.UpdateImage(imageId, "https://cdn.example.com/a.jpg", null);

        Assert.That(product.Images.Single().AltText, Is.Null);
    }

    [Test]
    public void UpdateImage_PreservesDisplayOrderAndMainFlag()
    {
        // The reason this method exists rather than "delete and re-add": that loses both.
        var product = NewProduct();
        var first = product.AddImage("https://cdn.example.com/a.jpg", null, 0);
        product.AddImage("https://cdn.example.com/b.jpg", null, 7);

        product.UpdateImage(first, "https://cdn.example.com/c.jpg", null);

        var image = product.Images.Single(i => i.Id == first);
        Assert.Multiple(() =>
        {
            Assert.That(image.DisplayOrder, Is.EqualTo(0));
            Assert.That(image.IsMain, Is.True, "the first image added is the main one");
        });
    }

    [Test]
    public void UpdateImage_ToAUrlAnotherImageHolds_IsRefused()
    {
        var product = NewProduct();
        var first = product.AddImage("https://cdn.example.com/a.jpg", null, 0);
        product.AddImage("https://cdn.example.com/b.jpg", null, 1);

        Assert.Throws<DomainException>(
            () => product.UpdateImage(first, "https://cdn.example.com/B.JPG", null),
            "duplicate detection is case-insensitive, matching AddImage");
    }

    [Test]
    public void UpdateImage_ResendingItsOwnUrl_IsAllowed()
    {
        // What an admin form does on every save. A duplicate check that forgot to exclude the
        // image being edited would refuse every edit that changed only the alt text.
        var product = NewProduct();
        var imageId = product.AddImage("https://cdn.example.com/a.jpg", null, 0);

        product.UpdateImage(imageId, "https://cdn.example.com/a.jpg", "Just the alt");

        Assert.That(product.Images.Single().AltText, Is.EqualTo("Just the alt"));
    }

    [Test]
    public void UpdateImage_WithAnUnknownId_IsRefused()
    {
        var product = NewProduct();
        product.AddImage("https://cdn.example.com/a.jpg", null, 0);

        Assert.Throws<DomainException>(() => product.UpdateImage(Guid.NewGuid(), "https://cdn.example.com/b.jpg", null));
    }

    [TestCase("not-a-url")]
    [TestCase("ftp://cdn.example.com/a.jpg")]
    [TestCase("")]
    public void UpdateImage_WithAnInvalidUrl_IsRefused(string url)
    {
        // The edited image must not be able to reach a state a new one could not.
        var product = NewProduct();
        var imageId = product.AddImage("https://cdn.example.com/a.jpg", null, 0);

        Assert.Throws<DomainException>(() => product.UpdateImage(imageId, url, null));
    }

    [Test]
    public void UpdateImage_OnADeletedProduct_IsRefused()
    {
        var product = NewProduct();
        var imageId = product.AddImage("https://cdn.example.com/a.jpg", null, 0);
        product.SoftDelete();

        Assert.Throws<DomainException>(() => product.UpdateImage(imageId, "https://cdn.example.com/b.jpg", null));
    }

    #endregion

    #region ReorderImages

    [Test]
    public void ReorderImages_AssignsDisplayOrderFromPosition()
    {
        var product = NewProduct();
        var a = product.AddImage("https://cdn.example.com/a.jpg", null, 0);
        var b = product.AddImage("https://cdn.example.com/b.jpg", null, 1);
        var c = product.AddImage("https://cdn.example.com/c.jpg", null, 2);

        product.ReorderImages([c, a, b]);

        Assert.Multiple(() =>
        {
            Assert.That(product.Images.Single(i => i.Id == c).DisplayOrder, Is.EqualTo(0));
            Assert.That(product.Images.Single(i => i.Id == a).DisplayOrder, Is.EqualTo(1));
            Assert.That(product.Images.Single(i => i.Id == b).DisplayOrder, Is.EqualTo(2));
        });
    }

    [Test]
    public void ReorderImages_DoesNotChangeWhichImageIsMain()
    {
        // Position and "is the main image" are independent. Only RemoveImage elects a main by
        // order, and it does so because the previous main no longer exists.
        var product = NewProduct();
        var a = product.AddImage("https://cdn.example.com/a.jpg", null, 0);
        var b = product.AddImage("https://cdn.example.com/b.jpg", null, 1);

        product.ReorderImages([b, a]);

        Assert.That(product.Images.Single(i => i.IsMain).Id, Is.EqualTo(a), "a was main before the reorder");
    }

    [Test]
    public void ReorderImages_WithAMissingId_IsRefused()
    {
        var product = NewProduct();
        var a = product.AddImage("https://cdn.example.com/a.jpg", null, 0);
        product.AddImage("https://cdn.example.com/b.jpg", null, 1);

        Assert.Throws<DomainException>(() => product.ReorderImages([a]),
            "a partial reorder has no correct answer for where the omitted images land");
    }

    [Test]
    public void ReorderImages_WithAnExtraId_IsRefused()
    {
        var product = NewProduct();
        var a = product.AddImage("https://cdn.example.com/a.jpg", null, 0);

        Assert.Throws<DomainException>(() => product.ReorderImages([a, Guid.NewGuid()]));
    }

    [Test]
    public void ReorderImages_WithADuplicateId_IsRefused()
    {
        var product = NewProduct();
        var a = product.AddImage("https://cdn.example.com/a.jpg", null, 0);
        product.AddImage("https://cdn.example.com/b.jpg", null, 1);

        // Count matches, so only the distinct check can catch this one.
        Assert.Throws<DomainException>(() => product.ReorderImages([a, a]));
    }

    [Test]
    public void ReorderImages_WithAnIdFromAnotherProduct_IsRefused()
    {
        var product = NewProduct();
        var a = product.AddImage("https://cdn.example.com/a.jpg", null, 0);
        var other = NewProduct();
        var foreignId = other.AddImage("https://cdn.example.com/x.jpg", null, 0);

        Assert.Throws<DomainException>(() => product.ReorderImages([a, foreignId]));
    }

    [Test]
    public void ReorderImages_WithEmptyGuid_ThrowsDomainException_NotInvalidOperation()
    {
        // Guid.Empty is the trap: a FirstOrDefault-based "unknown id" check returns Guid.Empty for
        // "nothing unknown found", so a supplied Guid.Empty would read as "all ids known" and fall
        // through to Single(), throwing InvalidOperationException — a 500 where 400 is owed.
        var product = NewProduct();
        product.AddImage("https://cdn.example.com/a.jpg", null, 0);

        Assert.Throws<DomainException>(() => product.ReorderImages([Guid.Empty]));
    }

    [Test]
    public void ReorderImages_OnADeletedProduct_IsRefused()
    {
        var product = NewProduct();
        var a = product.AddImage("https://cdn.example.com/a.jpg", null, 0);
        product.SoftDelete();

        Assert.Throws<DomainException>(() => product.ReorderImages([a]));
    }

    #endregion

    #region UpdateAttribute / RemoveAttribute

    [Test]
    public void UpdateAttribute_ReplacesNameAndValue()
    {
        var product = NewProduct();
        var id = product.AddAttribute("Colour", "Red");

        product.UpdateAttribute(id, "Color", "Blue");

        var attribute = product.Attributes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(attribute.Name, Is.EqualTo("Color"));
            Assert.That(attribute.Value, Is.EqualTo("Blue"));
        });
    }

    [Test]
    public void UpdateAttribute_KeepsItsId()
    {
        var product = NewProduct();
        var id = product.AddAttribute("Color", "Red");

        product.UpdateAttribute(id, "Color", "Blue");

        Assert.That(product.Attributes.Single().Id, Is.EqualTo(id));
    }

    [Test]
    public void UpdateAttribute_ToANameASiblingHolds_IsRefused()
    {
        var product = NewProduct();
        var size = product.AddAttribute("Size", "L");
        product.AddAttribute("Color", "Red");

        Assert.Throws<DomainException>(() => product.UpdateAttribute(size, "COLOR", "Blue"),
            "collision detection is case-insensitive, matching AddAttribute");
    }

    [Test]
    public void UpdateAttribute_KeepingItsOwnName_IsAllowed()
    {
        var product = NewProduct();
        var id = product.AddAttribute("Color", "Red");

        product.UpdateAttribute(id, "Color", "Blue");

        Assert.That(product.Attributes.Single().Value, Is.EqualTo("Blue"));
    }

    [Test]
    public void UpdateAttribute_WithAnUnknownId_IsRefused()
    {
        var product = NewProduct();
        product.AddAttribute("Color", "Red");

        Assert.Throws<DomainException>(() => product.UpdateAttribute(Guid.NewGuid(), "Size", "L"));
    }

    [TestCase("", "Red")]
    [TestCase("   ", "Red")]
    [TestCase("Color", "")]
    [TestCase("Color", "   ")]
    public void UpdateAttribute_WithBlankNameOrValue_IsRefused(string name, string value)
    {
        var product = NewProduct();
        var id = product.AddAttribute("Color", "Red");

        Assert.Throws<DomainException>(() => product.UpdateAttribute(id, name, value));
    }

    [Test]
    public void RemoveAttribute_RemovesIt()
    {
        var product = NewProduct();
        var id = product.AddAttribute("Color", "Red");
        product.AddAttribute("Size", "L");

        product.RemoveAttribute(id);

        Assert.That(product.Attributes.Select(a => a.Name), Is.EquivalentTo(new[] { "Size" }));
    }

    [Test]
    public void RemoveAttribute_WithAnUnknownId_IsRefused()
    {
        var product = NewProduct();
        product.AddAttribute("Color", "Red");

        Assert.Throws<DomainException>(() => product.RemoveAttribute(Guid.NewGuid()));
    }

    [Test]
    public void RemoveAttribute_FreesRoomUnderTheCap()
    {
        // Attributes were add-only before S3, which made the 50 cap a one-way ratchet.
        var product = NewProduct();
        var ids = Enumerable.Range(0, 50).Select(i => product.AddAttribute($"Attr{i}", "v")).ToList();

        Assert.Throws<DomainException>(() => product.AddAttribute("OneTooMany", "v"));

        product.RemoveAttribute(ids[0]);
        Assert.DoesNotThrow(() => product.AddAttribute("OneTooMany", "v"));
    }

    #endregion

    #region ReplaceAttributes

    [Test]
    public void ReplaceAttributes_AddsRemovesAndUpdatesInOneCall()
    {
        var product = NewProduct();
        product.AddAttribute("Color", "Red");
        product.AddAttribute("Size", "L");

        product.ReplaceAttributes([("Color", "Blue"), ("Material", "Cotton")]);

        Assert.That(
            product.Attributes.Select(a => (a.Name, a.Value)),
            Is.EquivalentTo(new[] { ("Color", "Blue"), ("Material", "Cotton") }));
    }

    [Test]
    public void ReplaceAttributes_KeepsTheIdOfAnAttributeItRetains()
    {
        // The load-bearing property of reconciling rather than clearing and re-adding. Clearing
        // would mint a fresh Guid for "Color" on every save, and — worse — would put a DELETE and
        // an INSERT of the same (ProductId, lower(Name)) key in one SaveChanges, which M1's
        // non-deferrable unique index can reject with 23505 depending on the order EF emits them.
        var product = NewProduct();
        var colorId = product.AddAttribute("Color", "Red");

        product.ReplaceAttributes([("Color", "Blue")]);

        Assert.That(product.Attributes.Single().Id, Is.EqualTo(colorId));
    }

    [Test]
    public void ReplaceAttributes_MatchingNameCaseInsensitively_UpdatesRatherThanDuplicating()
    {
        var product = NewProduct();
        var id = product.AddAttribute("color", "Red");

        product.ReplaceAttributes([("COLOR", "Blue")]);

        var attribute = product.Attributes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(attribute.Id, Is.EqualTo(id), "it is the same attribute, not a second one");
            Assert.That(attribute.Name, Is.EqualTo("COLOR"), "the submitted casing wins");
            Assert.That(attribute.Value, Is.EqualTo("Blue"));
        });
    }

    [Test]
    public void ReplaceAttributes_WithAnEmptyList_ClearsThemAll()
    {
        var product = NewProduct();
        product.AddAttribute("Color", "Red");
        product.AddAttribute("Size", "L");

        product.ReplaceAttributes([]);

        Assert.That(product.Attributes, Is.Empty);
    }

    [Test]
    public void ReplaceAttributes_TrimsNamesAndValues()
    {
        var product = NewProduct();

        product.ReplaceAttributes([("  Color  ", "  Red  ")]);

        var attribute = product.Attributes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(attribute.Name, Is.EqualTo("Color"));
            Assert.That(attribute.Value, Is.EqualTo("Red"));
        });
    }

    [Test]
    public void ReplaceAttributes_WithADuplicateName_IsRefused()
    {
        var product = NewProduct();

        Assert.Throws<DomainException>(() => product.ReplaceAttributes([("Color", "Red"), ("color", "Blue")]),
            "which of the two wins is not something the aggregate should pick silently");
    }

    [Test]
    public void ReplaceAttributes_AboveTheCap_IsRefused()
    {
        var product = NewProduct();
        var tooMany = Enumerable.Range(0, 51).Select(i => ($"Attr{i}", "v")).ToList();

        Assert.Throws<DomainException>(() => product.ReplaceAttributes(tooMany));
    }

    [Test]
    public void ReplaceAttributes_AtExactlyTheCap_IsAllowed()
    {
        var product = NewProduct();
        var exactly = Enumerable.Range(0, 50).Select(i => ($"Attr{i}", "v")).ToList();

        product.ReplaceAttributes(exactly);

        Assert.That(product.Attributes, Has.Count.EqualTo(50));
    }

    [Test]
    public void ReplaceAttributes_RejectedForTheCap_ChangesNothing()
    {
        // TransactionBehavior commits on any non-exception return, so validation that can reject
        // must do so before anything mutates. A DomainException is not that path — it rolls back —
        // but the aggregate should still not be left half-modified for the caller that catches it.
        var product = NewProduct();
        product.AddAttribute("Color", "Red");
        var tooMany = Enumerable.Range(0, 51).Select(i => ($"Attr{i}", "v")).ToList();

        Assert.Throws<DomainException>(() => product.ReplaceAttributes(tooMany));

        Assert.That(product.Attributes.Select(a => a.Name), Is.EquivalentTo(new[] { "Color" }));
    }

    [TestCase("", "v")]
    [TestCase("   ", "v")]
    [TestCase("Color", "")]
    public void ReplaceAttributes_WithABlankNameOrValue_IsRefused(string name, string value)
    {
        var product = NewProduct();

        Assert.Throws<DomainException>(() => product.ReplaceAttributes([(name, value)]));
    }

    [Test]
    public void ReplaceAttributes_OnADeletedProduct_IsRefused()
    {
        var product = NewProduct();
        product.SoftDelete();

        Assert.Throws<DomainException>(() => product.ReplaceAttributes([("Color", "Red")]));
    }

    [Test]
    public void ReplaceAttributes_RaisesNoDomainEvent()
    {
        var product = NewProduct();
        product.ClearDomainEvents();

        product.ReplaceAttributes([("Color", "Red")]);
        product.UpdateImage(product.AddImage("https://cdn.example.com/a.jpg", null, 0), "https://cdn.example.com/b.jpg", null);

        Assert.That(product.DomainEvents, Is.Empty);
    }

    #endregion
}
