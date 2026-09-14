namespace EShop.Basket.Infrastructure.Outbox;

public static class BasketOutboxKeys
{
    /// <summary>Messages ready to publish. Pushed on the left, claimed from the right.</summary>
    public const string Pending = "basket:outbox:pending";

    /// <summary>Messages a processor has claimed; each has a lease while it is being published.</summary>
    public const string Processing = "basket:outbox:processing";

    /// <summary>
    /// Sorted set of messages waiting to be retried, scored by the Unix time (ms) they fall due (Basket audit S7, H6).
    /// </summary>
    public const string Retry = "basket:outbox:retry";

    /// <summary>
    /// Messages that exhausted their attempts. Each is an order Ordering never received, so they are kept until an
    /// admin replays them (D7), never expired.
    /// </summary>
    public const string DeadLetter = "basket:outbox:dead";

    public const string LeasePrefix = "basket:outbox:lease:";

    public static string Lease(Guid messageId) => $"{LeasePrefix}{messageId}";
}
