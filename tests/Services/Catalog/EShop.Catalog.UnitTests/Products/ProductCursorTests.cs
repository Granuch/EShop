using System.Buffers.Text;
using System.Text;
using EShop.Catalog.Application.Products.Queries.GetNewestProducts;

namespace EShop.Catalog.UnitTests.Products;

[TestFixture]
public class ProductCursorTests
{
    [Test]
    public void Encode_ThenDecode_RoundTripsBothHalvesExactly()
    {
        // Microsecond-aligned, as a value read back from Postgres always is.
        var original = new ProductCursor(new DateTime(2026, 9, 10, 12, 34, 56, 789, 123, DateTimeKind.Utc), Guid.NewGuid());

        var ok = ProductCursor.TryDecode(original.Encode(), out var decoded);

        Assert.That(ok, Is.True);
        Assert.That(decoded, Is.EqualTo(original));
        Assert.That(decoded.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc),
            "Npgsql rejects a non-UTC DateTime parameter against a timestamptz column");
    }

    [Test]
    public void Encode_IsUrlSafe()
    {
        var encoded = new ProductCursor(DateTime.UtcNow, Guid.NewGuid()).Encode();

        Assert.That(encoded, Does.Not.Contain("+").And.Not.Contain("/").And.Not.Contain("="),
            "the cursor travels in a query string and must not need escaping");
    }

    [Test]
    public void Encode_TreatsUnspecifiedKindAsTheUtcItCameFrom()
    {
        var utc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var id = Guid.NewGuid();

        var fromUnspecified = new ProductCursor(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), id).Encode();

        Assert.That(fromUnspecified, Is.EqualTo(new ProductCursor(utc, id).Encode()));
    }

    private static string B64(string raw) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));

    [TestCase("")]
    [TestCase("not base64 at all!")]
    public void TryDecode_RejectsNonBase64(string value)
    {
        Assert.That(ProductCursor.TryDecode(value, out _), Is.False);
    }

    [Test]
    public void TryDecode_RejectsNull()
    {
        Assert.That(ProductCursor.TryDecode(null, out _), Is.False);
    }

    [Test]
    public void TryDecode_RejectsWellFormedBase64WithTheWrongContent()
    {
        // The shapes a hand-built or truncated cursor takes. Each must be refused, not read as
        // "no cursor" — silently restarting from page one is the defect this type replaces.
        Assert.Multiple(() =>
        {
            Assert.That(ProductCursor.TryDecode(B64("no separator"), out _), Is.False, "no separator");
            Assert.That(ProductCursor.TryDecode(B64($":{Guid.NewGuid():N}"), out _), Is.False, "missing ticks");
            Assert.That(ProductCursor.TryDecode(B64($"-5:{Guid.NewGuid():N}"), out _), Is.False, "negative ticks");
            Assert.That(ProductCursor.TryDecode(B64($"{long.MaxValue}:{Guid.NewGuid():N}"), out _), Is.False, "ticks past DateTime.MaxValue");
            Assert.That(ProductCursor.TryDecode(B64("638000000000000000:not-a-guid"), out _), Is.False, "bad id");
            Assert.That(ProductCursor.TryDecode(B64($"638000000000000000:{Guid.NewGuid():D}"), out _), Is.False, "id in the wrong format");
            Assert.That(ProductCursor.TryDecode(new string('A', 200), out _), Is.False, "overlong");
        });
    }

    [Test]
    public void Decode_ThrowsOnGarbage_RatherThanReturningAStartingPosition()
    {
        Assert.Throws<FormatException>(() => ProductCursor.Decode("garbage"));
    }
}
