namespace EShop.Basket.Infrastructure.Configuration;

public class RedisBasketOptions
{
    public const string SectionName = "BasketRedis";

    public string BasketKeyPrefix { get; set; } = "basket:user:";
    public string ProductUsersKeyPrefix { get; set; } = "basket:product:";
    public TimeSpan BasketTtl { get; set; } = TimeSpan.FromDays(7);
}
