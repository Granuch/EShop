namespace EShop.Basket.Infrastructure.Configuration;

public class RedisBasketOptions
{
    public const string SectionName = "BasketRedis";

    public string BasketKeyPrefix { get; set; } = "basket:user:";
    public string ProductUsersKeyPrefix { get; set; } = "basket:product:";
    public TimeSpan BasketTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Where an unreadable basket document is kept when a write replaces it (Basket audit L6, S9).</summary>
    public string CorruptBasketKeyPrefix { get; set; } = "basket:corrupt:";

    /// <summary>How long a kept unreadable document stays, long enough to be found and examined.</summary>
    public TimeSpan CorruptBasketRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>The key of a user's basket document. The repository and checkout's commit must agree on it.</summary>
    public string BasketKey(string userId) => $"{BasketKeyPrefix}{userId}";

    /// <summary>The reverse-index set of users whose basket holds the product, which price sync fans out over.</summary>
    public string ProductUsersKey(Guid productId) => $"{ProductUsersKeyPrefix}{productId}:users";

    /// <summary>The list of a user's unreadable basket documents that writes replaced, newest first.</summary>
    public string CorruptBasketKey(string userId) => $"{CorruptBasketKeyPrefix}{userId}";

    /// <summary>When the newest price change applied for the product happened (Basket audit M10, S9).</summary>
    public string ProductPriceChangedAtKey(Guid productId) => $"{ProductUsersKeyPrefix}{productId}:price-changed-at";
}
