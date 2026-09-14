using System.Security.Cryptography;
using System.Text;

namespace EShop.Identity.Domain.Security;

/// <summary>
/// Hashes a refresh token for storage and lookup.
///
/// Refresh tokens used to be stored raw in <c>refresh_tokens.Token</c>, so read access to that
/// one table was full session takeover for every user with a live session — no cracking, no
/// further access needed, just replay (SEC-04). They are now stored only as a hash: a stolen
/// database row can no longer be presented as a token.
///
/// <para>
/// <b>Why a plain SHA-256 and not bcrypt/PBKDF2.</b> A slow KDF buys resistance to guessing,
/// which matters for passwords because they are low-entropy and human-chosen. A refresh token
/// here is 64 bytes straight from <see cref="RandomNumberGenerator"/> — there is nothing to
/// guess, and no dictionary to run. A fast hash is the right primitive for a high-entropy
/// secret, and being fast matters: this runs on every token refresh.
/// </para>
///
/// <para>
/// The output is lowercase hex, so it is always exactly 64 characters and maps to a fixed-width
/// <c>char(64)</c> column with a unique index. Note this is a different encoding from
/// <c>RevokedTokenCache</c>'s cache key, which base64-encodes the same digest and truncates to
/// 16 characters — that one is a cache key, not an identity, and the two must not be conflated.
/// </para>
/// </summary>
public static class RefreshTokenHasher
{
    /// <summary>Length of the hex digest, and of the database column.</summary>
    public const int HashLength = 64;

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>
    /// Hashes an optional token, preserving null. Used for <c>ReplacedByTokenHash</c>, which is
    /// absent until the token is rotated.
    /// </summary>
    public static string? HashOrNull(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : Hash(token);
}
