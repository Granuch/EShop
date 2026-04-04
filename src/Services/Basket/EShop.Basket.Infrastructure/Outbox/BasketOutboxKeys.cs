namespace EShop.Basket.Infrastructure.Outbox;

public static class BasketOutboxKeys
{
    public const string Pending = "basket:outbox:pending";
    public const string Processing = "basket:outbox:processing";
    public const string DeadLetter = "basket:outbox:dead";
}
