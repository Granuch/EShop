using EShop.Identity.Domain.Security;

namespace EShop.Identity.UnitTests.Domain;

/// <summary>
/// TEST-06. <see cref="IdentifierHasher"/> produces every log correlation id, brute-force tracking
/// key and cache partition key in this service, and had <b>no tests at all</b> before Stage 8 —
/// the whole Domain layer did not.
///
/// <para>
/// The properties that matter are not "does SHA-256 work" but the normalisation rules layered on
/// top of it. Case and whitespace handling decide whether two spellings of the same email are
/// tracked as one account or two, which is the difference between brute-force protection working
/// and being bypassable by capitalising a letter.
/// </para>
/// </summary>
[TestFixture]
public class IdentifierHasherTests
{
    [Test]
    public void Hash_ProducesA64CharacterHexDigest()
    {
        var hash = IdentifierHasher.Hash("user@test.com");

        Assert.That(hash, Has.Length.EqualTo(64));
        Assert.That(hash, Does.Match("^[0-9A-F]{64}$"), "Convert.ToHexString produces uppercase hex");
    }

    [Test]
    public void Hash_IsDeterministic()
    {
        Assert.That(IdentifierHasher.Hash("user@test.com"), Is.EqualTo(IdentifierHasher.Hash("user@test.com")));
    }

    /// <summary>
    /// The load-bearing one. Email addresses are case-insensitive, so if these hashed differently
    /// an attacker could reset their failed-attempt counter by varying capitalisation.
    /// </summary>
    [Test]
    public void Hash_IsCaseInsensitive()
    {
        Assert.That(
            IdentifierHasher.Hash("User@Test.COM"),
            Is.EqualTo(IdentifierHasher.Hash("user@test.com")));
    }

    [Test]
    public void Hash_IgnoresSurroundingWhitespace()
    {
        Assert.That(
            IdentifierHasher.Hash("  user@test.com  "),
            Is.EqualTo(IdentifierHasher.Hash("user@test.com")));
    }

    [Test]
    public void Hash_DistinguishesDifferentIdentifiers()
    {
        Assert.That(
            IdentifierHasher.Hash("a@test.com"),
            Is.Not.EqualTo(IdentifierHasher.Hash("b@test.com")));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Hash_RejectsMissingIdentifiers(string? identifier)
    {
        Assert.That(() => IdentifierHasher.Hash(identifier!), Throws.ArgumentException);
    }

    [Test]
    public void HashShort_IsThePrefixOfTheFullHash()
    {
        var full = IdentifierHasher.Hash("user@test.com");
        var shortHash = IdentifierHasher.HashShort("user@test.com");

        Assert.That(shortHash, Has.Length.EqualTo(16));
        Assert.That(full, Does.StartWith(shortHash));
    }

    [Test]
    public void HashComposite_IsOrderSensitive()
    {
        Assert.That(
            IdentifierHasher.HashComposite("user@test.com", "10.0.0.1"),
            Is.Not.EqualTo(IdentifierHasher.HashComposite("10.0.0.1", "user@test.com")));
    }

    [Test]
    public void HashComposite_NormalisesEachPartLikeHashDoes()
    {
        Assert.That(
            IdentifierHasher.HashComposite(" User@Test.COM ", "10.0.0.1"),
            Is.EqualTo(IdentifierHasher.HashComposite("user@test.com", "10.0.0.1")));
    }

    [Test]
    public void HashComposite_RejectsAnEmptyArgumentList()
    {
        Assert.That(() => IdentifierHasher.HashComposite(), Throws.ArgumentException);
    }

    /// <summary>
    /// Unlike <see cref="IdentifierHasher.Hash"/>, which throws on a null identifier,
    /// <c>HashComposite</c> coerces a null element to an empty string. Pinned because the
    /// inconsistency is invisible at the call site: the same bad input is a hard failure in one
    /// method and a silent empty segment in the other.
    /// </summary>
    [Test]
    public void HashComposite_TreatsANullPartAsEmptyRatherThanThrowing()
    {
        Assert.That(() => IdentifierHasher.HashComposite("user@test.com", null!), Throws.Nothing);
        Assert.That(
            IdentifierHasher.HashComposite("user@test.com", null!),
            Is.EqualTo(IdentifierHasher.HashComposite("user@test.com", "")));
    }

    /// <summary>
    /// Composite keys are joined with "|" and then hashed, so a single identifier that already
    /// contains the delimiter collides with the two-part composite of its halves. Harmless today —
    /// the inputs are emails and IP addresses, neither of which can contain "|" — but it is a real
    /// property of the scheme, and the thing that would break first if composite keys were ever
    /// built from free-form input.
    /// </summary>
    [Test]
    public void HashComposite_IsAmbiguousIfAnIdentifierCanContainTheDelimiter()
    {
        Assert.That(
            IdentifierHasher.HashComposite("a|b"),
            Is.EqualTo(IdentifierHasher.HashComposite("a", "b")));
    }
}
