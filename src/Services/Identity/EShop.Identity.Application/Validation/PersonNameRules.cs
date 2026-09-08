namespace EShop.Identity.Application.Validation;

/// <summary>
/// The single definition of what a person's name may contain.
///
/// <para>
/// This exists because the rule was previously written out four times — twice in
/// <c>RegisterCommandValidator</c> and twice in <c>UpdateProfileCommandValidator</c> — which meant
/// any change had to be made in lockstep across two files or registration and profile-update would
/// disagree about the same name. A user could then create an account and be unable to edit their
/// own profile, or vice versa.
/// </para>
/// </summary>
public static class PersonNameRules
{
    /// <summary>
    /// Any Unicode letter, plus combining marks, spaces, apostrophes and hyphens.
    ///
    /// <para>
    /// The rule was <c>^[a-zA-Z\s'-]+$</c> until it was widened here, which rejected every name
    /// outside 7-bit ASCII — <c>José</c>, <c>Müller</c>, <c>Иван</c>, <c>李</c> all failed with
    /// "can only contain letters". That was a defect rather than a control: this field is stored
    /// and rendered, never interpreted, so restricting it to ASCII bought no safety and locked out
    /// a large share of real customers.
    /// </para>
    ///
    /// <para>
    /// <c>\p{M}</c> is not optional. A precomposed <c>José</c> is one <c>\p{L}</c> code point
    /// (U+00E9), but the decomposed form is <c>e</c> followed by U+0301, a combining mark — and
    /// which one arrives depends on the client's normalisation, not on the user. Without
    /// <c>\p{M}</c> the same name is accepted or rejected depending on which keyboard or browser
    /// typed it.
    /// </para>
    ///
    /// <para>
    /// What this still excludes is the part that was ever doing security work: digits, punctuation,
    /// and — because they are category <c>\p{C}</c>, not <c>\p{L}</c> or <c>\p{M}</c> — the
    /// invisible formatting characters that make display spoofing possible, such as zero-width
    /// joiners and the right-to-left override U+202E. Widening this further to <c>\p{C}</c> or to
    /// a bare <c>.</c> would give that up.
    /// </para>
    /// </summary>
    public const string Pattern = @"^[\p{L}\p{M}\s'\-]+$";

    /// <summary>
    /// Shared message so the two validators cannot describe the same rule differently.
    /// </summary>
    public const string Message = "can only contain letters, spaces, hyphens, and apostrophes";

    /// <summary>Matches the <c>varchar(50)</c> columns in <c>IdentityDbContext</c>.</summary>
    public const int MaxLength = 50;
}
