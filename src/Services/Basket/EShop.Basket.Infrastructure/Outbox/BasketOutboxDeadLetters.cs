using System.Text.Json;
using EShop.Basket.Domain.Interfaces;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Outbox;

/// <summary>
/// The dead-letter list and the way back out of it (Basket audit S7, H6; D7). A dead-lettered checkout is the only
/// remaining record of an order — the basket was deleted when it was queued — so dead letters are kept until they are
/// replayed, never expired, and outbox health reports Degraded while any exist.
/// </summary>
public sealed class BasketOutboxDeadLetters : IOutboxDeadLetterReader
{
    /// <summary>
    /// KEYS: dead, pending. Moves the oldest dead letter to pending with its retry count reset to 0, in one step, so a
    /// crash cannot lose it between the two lists. Returns 1, or nil when there are none.
    /// </summary>
    private const string ReplayScript = """
        local payload = redis.call('RPOP', KEYS[1])
        if not payload then return false end
        local ok, message = pcall(cjson.decode, payload)
        if ok and type(message) == 'table' then
          message.retryCount = 0
          payload = cjson.encode(message)
        end
        redis.call('LPUSH', KEYS[2], payload)
        return 1
        """;

    private readonly IDatabase _database;

    public BasketOutboxDeadLetters(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    public Task<long> CountAsync() => _database.ListLengthAsync(BasketOutboxKeys.DeadLetter);

    /// <summary>
    /// Admin panel S14 (#81): a page of dead letters, newest first (they are pushed on the left). The length and the
    /// page are read in one <c>MULTI</c>, so <see cref="OutboxDeadLetterPage.Total"/> describes the list the page came
    /// from. The payload — the order, shipping address included — is read here and never returned.
    /// </summary>
    public async Task<OutboxDeadLetterPage> ReadDeadLettersAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var transaction = _database.CreateTransaction();
        var total = transaction.ListLengthAsync(BasketOutboxKeys.DeadLetter);
        var page = transaction.ListRangeAsync(BasketOutboxKeys.DeadLetter, offset, (long)offset + limit - 1);
        await transaction.ExecuteAsync();

        return new OutboxDeadLetterPage(await total, (await page).Select(Describe).ToList());
    }

    private static OutboxDeadLetter Describe(RedisValue value)
    {
        RedisOutboxMessage? message;
        try
        {
            message = value.IsNullOrEmpty ? null : RedisOutboxMessage.Parse(value.ToString());
        }
        catch (JsonException)
        {
            message = null;
        }

        // The processor dead-letters an entry it cannot parse exactly as it found it; there is nothing to report on it.
        if (message is null)
        {
            return new OutboxDeadLetter(
                IsReadable: false, null, null, null, null, null, null, null, null);
        }

        return new OutboxDeadLetter(
            IsReadable: true,
            MessageId: message.Id,
            EventType: message.Type,
            OccurredOnUtc: message.OccurredOnUtc,
            DeadLetteredAtUtc: message.DeadLetteredAtUtc,
            Attempts: message.RetryCount == RedisOutboxMessage.UnpublishableRetryCount ? null : message.RetryCount,
            FailureReason: message.FailureReason,
            ExceptionType: message.ExceptionType,
            CorrelationId: message.CorrelationId);
    }

    /// <summary>Replays up to <paramref name="max"/> dead letters, oldest first. Returns how many were replayed.</summary>
    public async Task<int> ReplayAsync(int max)
    {
        var replayed = 0;

        while (replayed < max)
        {
            var result = await _database.ScriptEvaluateAsync(
                ReplayScript, [BasketOutboxKeys.DeadLetter, BasketOutboxKeys.Pending]);

            if (result.IsNull)
            {
                break;
            }

            replayed++;
        }

        return replayed;
    }
}
