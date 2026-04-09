using System.Threading.Channels;
using System.Threading;

namespace EShop.ApiGateway.Notifications;

public sealed class GatewayEmailQueue
{
    private const int Capacity = 2000;

    private readonly Channel<EmailNotificationContext> _channel = Channel.CreateBounded<EmailNotificationContext>(
        new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private long _enqueuedCount;
    private long _dequeuedCount;
    private long _dropCount;

    public ValueTask EnqueueAsync(EmailNotificationContext context, CancellationToken cancellationToken)
    {
        var enqueuedSoFar = Interlocked.Read(ref _enqueuedCount);
        var dequeuedSoFar = Interlocked.Read(ref _dequeuedCount);
        var backlog = enqueuedSoFar - dequeuedSoFar;

        if (backlog >= Capacity)
        {
            Interlocked.Increment(ref _dropCount);
        }

        var writeAccepted = _channel.Writer.TryWrite(context);
        if (writeAccepted)
        {
            Interlocked.Increment(ref _enqueuedCount);
            return ValueTask.CompletedTask;
        }

        Interlocked.Increment(ref _dropCount);
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<EmailNotificationContext> ReadAllAsync(CancellationToken cancellationToken)
    {
        return ReadWithDiagnosticsAsync(cancellationToken);
    }

    public GatewayEmailQueueSnapshot GetSnapshot()
    {
        var enqueued = Interlocked.Read(ref _enqueuedCount);
        var dequeued = Interlocked.Read(ref _dequeuedCount);
        var dropped = Interlocked.Read(ref _dropCount);
        var backlog = Math.Max(0, enqueued - dequeued);

        return new GatewayEmailQueueSnapshot(
            Capacity,
            enqueued,
            dequeued,
            dropped,
            backlog);
    }

    private async IAsyncEnumerable<EmailNotificationContext> ReadWithDiagnosticsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Increment(ref _dequeuedCount);
            yield return item;
        }
    }
}

public sealed record GatewayEmailQueueSnapshot(
    int Capacity,
    long EnqueuedCount,
    long DequeuedCount,
    long DroppedCount,
    long BacklogCount);
