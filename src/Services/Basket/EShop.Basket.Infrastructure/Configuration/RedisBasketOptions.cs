namespace EShop.Basket.Infrastructure.Configuration;

public class RedisBasketOptions
{
    public const string SectionName = "BasketRedis";

    public string BasketKeyPrefix { get; set; } = "basket:user:";
    public string ProductUsersKeyPrefix { get; set; } = "basket:product:";
    public TimeSpan BasketTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The key of a user's basket document. The repository and checkout's commit must agree on it.</summary>
    public string BasketKey(string userId) => $"{BasketKeyPrefix}{userId}";

    /// <summary>The reverse-index set of users whose basket holds the product, which price sync fans out over.</summary>
    public string ProductUsersKey(Guid productId) => $"{ProductUsersKeyPrefix}{productId}:users";
}
