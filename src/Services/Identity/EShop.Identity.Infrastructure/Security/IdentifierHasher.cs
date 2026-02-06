using DomainHasher = EShop.Identity.Domain.Security.IdentifierHasher;

namespace EShop.Identity.Infrastructure.Security;

/// <summary>
/// Delegates to the Domain layer IdentifierHasher.
/// Kept for backward compatibility with existing Infrastructure code.
/// </summary>
public static class IdentifierHasher
{
    public static string Hash(string identifier) => DomainHasher.Hash(identifier);

    public static string HashShort(string identifier) => DomainHasher.HashShort(identifier);

    public static string HashComposite(params string[] identifiers) => DomainHasher.HashComposite(identifiers);
}
