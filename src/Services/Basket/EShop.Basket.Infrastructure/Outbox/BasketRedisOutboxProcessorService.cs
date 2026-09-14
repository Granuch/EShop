using System.Collections.Concurrent;
using System.Text.Json;
using EShop.Basket.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Outbox;

/// <summary>
/// Drains Basket's Redis outbox into MassTransit.
///
/// <para><b>Every move between the outbox's lists is one Lua step (Basket audit S7).</b> Claiming a message and setting
/// its lease is one script, so the recovery sweep of another instance can no longer re-queue a message in the gap
/// between the two (L7). Moving a message out of <see cref="BasketOutboxKeys.Processing"/> — published, retried,
/// dead-lettered, recovered — happens only if it is still there, so two instances never both move it.</para>
///
/// <para><b>A failed publish waits (H6).</b> It goes to <see cref="BasketOutboxKeys.Retry"/>, due after the next delay in
/// <see cref="BasketOutboxOptions.RetryDelays"/>, and is promoted back to pending when due; after the last attempt it is
/// dead-lettered and kept until an admin replays it (D7). A publish that outlives
/// <see cref="BasketOutboxOptions.PublishTimeout"/> counts as a failed attempt; one cancelled because the service is
/// stopping does not, and goes back to pending unchanged.</para>
/// </summary>
public class BasketRedisOutboxProcessorService : BackgroundService
{
    /// <summary>KEYS: pending, processing. ARGV: lease prefix, lease value, lease TTL (ms). Returns the payload or nil.</summary>
    private const string ClaimScript = """
        local payload = redis.call('RPOPLPUSH', KEYS[1], KEYS[2])
        if not payload then return false end
        local ok, message = pcall(cjson.decode, payload)
        if ok and type(message) == 'table' and message.id then
          redis.call('SET', ARGV[1] .. message.id, ARGV[2], 'PX', ARGV[3])
        end
        return payload
        """;

    /// <summary>
    /// KEYS: processing, destination, lease. ARGV: payload, new payload, 'list' or 'zset', score. Moves the payload out of
    /// processing only if it is still there, and always drops the lease. Returns 1 if moved.
    /// </summary>
    private const string MoveScript = """
        local moved = redis.call('LREM', KEYS[1], 1, ARGV[1])
        if moved == 1 then
          if ARGV[3] == 'zset' then
            redis.call('ZADD', KEYS[2], ARGV[4], ARGV[2])
          else
            redis.call('LPUSH', KEYS[2], ARGV[2])
          end
        end
        redis.call('DEL', KEYS[3])
        return moved
        """;

    /// <summary>KEYS: retry, pending. ARGV: member. Moves one due retry to pending, once. Returns 1 if moved.</summary>
    private const string PromoteScript = """
        if redis.call('ZREM', KEYS[1], ARGV[1]) == 1 then
          redis.call('LPUSH', KEYS[2], ARGV[1])
          return 1
        end
        return 0
        """;

    private const string AllowedNamespacePrefix = "EShop.";
    private const int MaxTypeCacheEntries = 1000;
    private const int PromoteBatchSize = 100;

    /// <summary>A message whose type cannot be resolved can never be published; it is dead-lettered on sight.</summary>
    private const int UnpublishableRetryCount = int.MaxValue;

    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();

    private readonly IDatabase _database;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BasketOutboxOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BasketRedisOutboxProcessorService> _logger;
    private readonly IBasketMetrics _metrics;

    public BasketRedisOutboxProcessorService(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        BasketOutboxOptions options,
        TimeProvider time,
        ILogger<BasketRedisOutboxProcessorService> logger,
        IBasketMetrics metrics)
    {
        _database = redis.GetDatabase();
        _scopeFactory = scopeFactory;
        _options = options;
        _time = time;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Basket Redis outbox processor started");
        var nextRecoveryAt = _time.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_time.GetUtcNow() >= nextRecoveryAt)
                {
                    await RecoverStaleProcessingMessagesAsync(stoppingToken);
                    nextRecoveryAt = _time.GetUtcNow() + _options.RecoveryInterval;
                }

                await PromoteDueRetriesAsync();

                if (!await ProcessNextAsync(stoppingToken))
                {
                    await Task.Delay(_options.IdlePollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in basket outbox processor");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

        _logger.LogInformation("Basket Redis outbox processor stopped");
    }

    /// <summary>Claims and publishes one pending message. False if there was none to claim.</summary>
    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var claimed = await _database.ScriptEvaluateAsync(
            ClaimScript,
            [BasketOutboxKeys.Pending, BasketOutboxKeys.Processing],
            [BasketOutboxKeys.LeasePrefix, _time.GetUtcNow().ToString("O"), (long)_options.ProcessingLeaseTtl.TotalMilliseconds]);

        if (claimed.IsNull)
        {
            return false;
        }

        var payload = (string)claimed!;

        RedisOutboxMessage? message;
        try
        {
            message = RedisOutboxMessage.Parse(payload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Unreadable basket outbox entry. Moving it to the dead-letter list");
            message = null;
        }

        if (message == null)
        {
            await MoveOutOfProcessingAsync(payload, payload, BasketOutboxKeys.DeadLetter, leaseKey: null);
            _metrics.RecordOutboxRecovery("dead_letter");
            return true;
        }

        var leaseKey = BasketOutboxKeys.Lease(message.Id);
        var integrationEvent = TryReadEvent(message, out var eventType);
        if (integrationEvent == null || eventType == null)
        {
            _logger.LogError(
                "Outbox message {MessageId} has an unresolvable, disallowed or unreadable type '{Type}'. Moving it to the dead-letter list",
                message.Id, message.Type);
            var unpublishable = message with { RetryCount = UnpublishableRetryCount };
            await MoveOutOfProcessingAsync(payload, unpublishable.ToJson(), BasketOutboxKeys.DeadLetter, leaseKey);
            _metrics.RecordOutboxRecovery("dead_letter");
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var publishEndpoint = scope.ServiceProvider.GetService<IPublishEndpoint>();
        if (publishEndpoint == null)
        {
            _logger.LogWarning("IPublishEndpoint is not registered. Outbox processor is pausing until messaging is available.");
            await MoveOutOfProcessingAsync(payload, payload, BasketOutboxKeys.Pending, leaseKey);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return false;
        }

        try
        {
            using var publishTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            publishTimeout.CancelAfter(_options.PublishTimeout);

            await publishEndpoint.Publish(
                integrationEvent,
                eventType,
                Pipe.Execute<PublishContext>(context =>
                {
                    context.MessageId = message.Id;
                    if (Guid.TryParse(message.CorrelationId, out var correlationId))
                    {
                        context.CorrelationId = correlationId;
                    }
                }),
                publishTimeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The service is stopping: not a failed attempt. Back to pending exactly as it was.
            await MoveOutOfProcessingAsync(payload, payload, BasketOutboxKeys.Pending, leaseKey);
            throw;
        }
        catch (Exception ex)
        {
            await RetryOrDeadLetterAsync(payload, message, leaseKey, ex);
            return true;
        }

        var completion = _database.CreateTransaction();
        _ = completion.ListRemoveAsync(BasketOutboxKeys.Processing, payload, count: 1);
        _ = completion.KeyDeleteAsync(leaseKey);
        await completion.ExecuteAsync();

        _logger.LogInformation("Published basket outbox message {MessageId} of type {Type}", message.Id, message.Type);
        return true;
    }

    /// <summary>Moves every retry whose delay has passed back to pending. Returns how many it moved.</summary>
    internal async Task<int> PromoteDueRetriesAsync()
    {
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        var due = await _database.SortedSetRangeByScoreAsync(
            BasketOutboxKeys.Retry, double.NegativeInfinity, now, take: PromoteBatchSize);

        var promoted = 0;
        foreach (var member in due)
        {
            var moved = await _database.ScriptEvaluateAsync(
                PromoteScript, [BasketOutboxKeys.Retry, BasketOutboxKeys.Pending], [member]);

            if ((long)moved == 1)
            {
                promoted++;
            }
        }

        return promoted;
    }

    /// <summary>
    /// Re-queues messages left in processing by a processor that died: those whose lease has expired. A message a live
    /// processor is publishing keeps its lease, set in the same step that claimed it.
    /// </summary>
    internal async Task RecoverStaleProcessingMessagesAsync(CancellationToken cancellationToken)
    {
        var payloads = await _database.ListRangeAsync(BasketOutboxKeys.Processing, 0, 200);
        var recovered = 0;

        foreach (var value in payloads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (value.IsNullOrEmpty)
            {
                continue;
            }

            var payload = value.ToString();

            RedisOutboxMessage? message;
            try
            {
                message = RedisOutboxMessage.Parse(payload);
            }
            catch (JsonException)
            {
                message = null;
            }

            if (message == null)
            {
                if (await MoveOutOfProcessingAsync(payload, payload, BasketOutboxKeys.DeadLetter, leaseKey: null))
                {
                    _metrics.RecordOutboxRecovery("dead_letter");
                }

                continue;
            }

            var leaseKey = BasketOutboxKeys.Lease(message.Id);
            if (await _database.KeyExistsAsync(leaseKey))
            {
                continue;
            }

            if (await MoveOutOfProcessingAsync(payload, payload, BasketOutboxKeys.Pending, leaseKey))
            {
                recovered++;
                _metrics.RecordOutboxRecovery("recovered");
            }
        }

        if (recovered > 0)
        {
            _logger.LogWarning("Recovered {RecoveredCount} stale outbox messages back to pending queue", recovered);
        }
    }

    private async Task RetryOrDeadLetterAsync(string payload, RedisOutboxMessage message, string leaseKey, Exception error)
    {
        var next = message with { RetryCount = message.RetryCount + 1 };

        if (next.RetryCount >= _options.MaxAttempts)
        {
            await MoveOutOfProcessingAsync(payload, next.ToJson(), BasketOutboxKeys.DeadLetter, leaseKey);
            _metrics.RecordOutboxRecovery("dead_letter");
            _logger.LogError(error,
                "Basket outbox message {MessageId} failed its last attempt ({Attempts}) and was dead-lettered. It is an order Ordering has not received; replay it once the cause is fixed",
                next.Id, next.RetryCount);
            return;
        }

        var delay = _options.RetryDelays[next.RetryCount - 1];
        var dueAt = _time.GetUtcNow() + delay;
        await MoveOutOfProcessingAsync(payload, next.ToJson(), BasketOutboxKeys.Retry, leaseKey, dueAt.ToUnixTimeMilliseconds());
        _metrics.RecordOutboxRecovery("retry_scheduled");
        _logger.LogWarning(error,
            "Failed to publish basket outbox message {MessageId} (attempt {Attempt} of {MaxAttempts}); retrying in {Delay}",
            next.Id, next.RetryCount, _options.MaxAttempts, delay);
    }

    private async Task<bool> MoveOutOfProcessingAsync(
        string payload,
        string newPayload,
        string destination,
        string? leaseKey,
        long? dueAtUnixMs = null)
    {
        var moved = await _database.ScriptEvaluateAsync(
            MoveScript,
            [BasketOutboxKeys.Processing, destination, leaseKey ?? BasketOutboxKeys.LeasePrefix + "none"],
            [payload, newPayload, dueAtUnixMs.HasValue ? "zset" : "list", dueAtUnixMs ?? 0]);

        return (long)moved == 1;
    }

    private static object? TryReadEvent(RedisOutboxMessage message, out Type? eventType)
    {
        eventType = ResolveType(message.Type);
        if (eventType == null)
        {
            return null;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize(message.Payload, eventType, RedisOutboxMessage.JsonOptions);
            return deserialized is IIntegrationEvent ? deserialized : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Type? ResolveType(string fullName)
    {
        if (!fullName.StartsWith(AllowedNamespacePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (TypeCache.Count >= MaxTypeCacheEntries && !TypeCache.ContainsKey(fullName))
        {
            return null;
        }

        return TypeCache.GetOrAdd(fullName, static name =>
        {
            var directType = Type.GetType(name);
            if (directType != null)
            {
                return directType;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var resolved = assembly.GetType(name);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return null;
        });
    }
}
