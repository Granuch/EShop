using System.Security.Cryptography;
using System.Text;

namespace EShop.Identity.Infrastructure.Security;

/// <summary>
/// Provides privacy-preserving hashing for security-sensitive identifiers.
/// Used to pseudonymize usernames and emails in logs and cache keys.
/// </summary>
public static class IdentifierHasher
{
    /// <summary>
    /// Creates a SHA256 hash of an identifier (username or email) for privacy-preserving tracking.
    /// This prevents logging raw usernames/emails while still allowing correlation of events.
    /// 
    /// Security rationale:
    /// - SHA256 provides sufficient collision resistance for our use case
    /// - One-way hash prevents recovery of original identifier
    /// - Consistent output allows grouping attempts by account
    /// - No salt needed as this is for grouping, not password storage
    /// </summary>
    /// <param name="identifier">Username or email to hash</param>
    /// <returns>Hex-encoded SHA256 hash (64 characters)</returns>
    public static string Hash(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be null or whitespace", nameof(identifier));
        }

        // Normalize to lowercase to ensure consistent hashing
        // (email addresses are case-insensitive)
        var normalized = identifier.Trim().ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hashBytes = SHA256.HashData(bytes);

        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Creates a truncated hash suitable for use as a cache key or partition key.
    /// Uses first 16 characters (64 bits) for a good balance between collision resistance
    /// and key length.
    /// 
    /// Note: Truncation slightly increases collision probability, but 64 bits
    /// provides sufficient uniqueness for our caching use case (2^64 possible values).
    /// </summary>
    /// <param name="identifier">Username or email to hash</param>
    /// <returns>Truncated hash (16 characters)</returns>
    public static string HashShort(string identifier)
    {
        return Hash(identifier)[..16];
    }

    /// <summary>
    /// Creates a composite hash from multiple identifiers (e.g., username + IP).
    /// Used for creating composite tracking keys.
    /// </summary>
    /// <param name="identifiers">Identifiers to combine and hash</param>
    /// <returns>Hex-encoded SHA256 hash</returns>
    public static string HashComposite(params string[] identifiers)
    {
        if (identifiers == null || identifiers.Length == 0)
        {
            throw new ArgumentException("At least one identifier required", nameof(identifiers));
        }

        var combined = string.Join("|", identifiers.Select(i => i?.Trim().ToLowerInvariant() ?? ""));
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hashBytes = SHA256.HashData(bytes);

        return Convert.ToHexString(hashBytes);
    }
}
