using EShop.Identity.Application.Account.Commands.UpdateProfile;

namespace EShop.Identity.UnitTests.Account;

/// <summary>
/// TEST-06. The validator half of BUG-09's contract.
///
/// <para>
/// <c>UpdateProfileCommandHandler</c> treats an omitted <c>ProfilePictureUrl</c> as "leave the
/// stored picture alone" — but that behaviour is only reachable if the validator lets a null
/// through in the first place. The two halves live in different files and are edited
/// independently, so the handler's guard can be left intact while a well-meaning
/// <c>NotEmpty()</c> here makes it dead code and turns every partial update into a 400. That is
/// the repo-wide "optional members must tolerate explicit null" rule, and it has already bitten
/// <c>CreateProductCommand</c> in Catalog the same way.
/// </para>
/// </summary>
[TestFixture]
public class UpdateProfileCommandValidatorTests
{
    private UpdateProfileCommandValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new UpdateProfileCommandValidator();
    }

    private static UpdateProfileCommand Command(string? profilePictureUrl = null) => new()
    {
        UserId = "user-1",
        FirstName = "Ada",
        LastName = "Lovelace",
        ProfilePictureUrl = profilePictureUrl
    };

    // -------------------------------------------------------------------------------------
    // The optional-picture contract
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// System.Text.Json materialises an omitted optional property as null, so this is what an
    /// ordinary "rename me" request looks like on the wire. It must validate.
    /// </summary>
    [Test]
    public async Task ProfilePictureUrl_WhenOmitted_IsAccepted()
    {
        var result = await _validator.ValidateAsync(Command(profilePictureUrl: null));

        Assert.That(result.IsValid, Is.True,
            "a NotEmpty()/NotNull() rule here would 400 every partial update and make the "
            + "handler's BUG-09 guard unreachable");
    }

    /// <summary>The deliberate-clear path has to survive validation too, or a picture can never be removed.</summary>
    [Test]
    public async Task ProfilePictureUrl_WhenEmpty_IsAccepted()
    {
        var result = await _validator.ValidateAsync(Command(profilePictureUrl: string.Empty));

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task ProfilePictureUrl_WhenAValidHttpsUrl_IsAccepted()
    {
        var result = await _validator.ValidateAsync(Command("https://cdn.test/avatar.png"));

        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>
    /// The URL is rendered back into an <c>img</c> tag by clients, so a non-http scheme is the
    /// interesting rejection: <c>javascript:</c> and <c>data:</c> are the XSS-adjacent ones, and
    /// the scheme check — not the <c>Uri.TryCreate</c> call, which accepts both — is what stops them.
    /// </summary>
    [TestCase("javascript:alert(1)")]
    [TestCase("data:text/html;base64,PHNjcmlwdD4=")]
    [TestCase("ftp://files.test/avatar.png")]
    [TestCase("not-a-url")]
    [TestCase("/relative/path.png")]
    public async Task ProfilePictureUrl_WhenNotAnAbsoluteHttpUrl_IsRejected(string url)
    {
        var result = await _validator.ValidateAsync(Command(url));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == nameof(UpdateProfileCommand.ProfilePictureUrl)), Is.True);
    }

    [Test]
    public async Task ProfilePictureUrl_WhenLongerThanTheColumn_IsRejected()
    {
        var tooLong = "https://cdn.test/" + new string('a', 500) + ".png";

        var result = await _validator.ValidateAsync(Command(tooLong));

        Assert.That(result.IsValid, Is.False);
    }

    // -------------------------------------------------------------------------------------
    // Names
    // -------------------------------------------------------------------------------------

    [TestCase("")]
    [TestCase("   ")]
    public async Task FirstName_WhenBlank_IsRejected(string firstName)
    {
        var result = await _validator.ValidateAsync(Command() with { FirstName = firstName });

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == nameof(UpdateProfileCommand.FirstName)), Is.True);
    }

    [TestCase("O'Brien")]
    [TestCase("Mary-Jane")]
    [TestCase("Ada Lovelace")]
    public async Task Names_MayContainApostrophesHyphensAndSpaces(string name)
    {
        var result = await _validator.ValidateAsync(Command() with { FirstName = name, LastName = name });

        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>
    /// Pins a real limitation rather than endorsing it. The rule is `^[a-zA-Z\s'-]+$`, so any name
    /// outside 7-bit ASCII is rejected — "José", "Müller", "Иван", "李" all fail with "can only
    /// contain letters". For a storefront that is a genuine defect, not a security control: the
    /// field is stored and displayed, never interpreted. It is pinned here so that widening the
    /// rule to Unicode letters is a deliberate decision with a test to update, rather than
    /// something discovered by a customer who cannot save their own name.
    /// </summary>
    [TestCase("José")]
    [TestCase("Müller")]
    [TestCase("Иван")]
    public async Task Names_OutsideAsciiAreRejected_WhichIsAKnownLimitation(string name)
    {
        var result = await _validator.ValidateAsync(Command() with { FirstName = name });

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public async Task Names_LongerThanTheColumn_AreRejected()
    {
        var result = await _validator.ValidateAsync(Command() with { FirstName = new string('a', 51) });

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public async Task UserId_WhenEmpty_IsRejected()
    {
        var result = await _validator.ValidateAsync(Command() with { UserId = string.Empty });

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == nameof(UpdateProfileCommand.UserId)), Is.True);
    }
}
