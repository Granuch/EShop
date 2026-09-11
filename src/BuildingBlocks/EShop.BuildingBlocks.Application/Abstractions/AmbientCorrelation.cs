namespace EShop.BuildingBlocks.Application.Abstractions;

/// <summary>
/// M7 (Catalog audit Stage 7). The correlation id of work that did not arrive over HTTP — an outbox
/// row being dispatched, a broker message being consumed — held in async-local storage so that
/// <see cref="ICurrentUserContext.CorrelationId"/> reports it instead of minting a fresh one.
///
/// <para>
/// Without this, every domain-event handler dispatched by <c>OutboxProcessorService</c> read a
/// correlation id that belonged to nothing: the processor's scope has no <c>HttpContext</c>, so
/// <c>HttpCurrentUserContext</c> generated a new GUID, and the integration event the handler enqueued
/// carried that instead of the id of the request that caused it — even though the outbox row being
/// dispatched still held the right one. Traces broke at exactly the service boundary they exist to
/// cross. Fixing it here rather than in each handler fixes all of them (Catalog's two, Ordering's
/// four) and every handler written later.
/// </para>
///
/// <para>
/// Async-local rather than a scoped service because the processor dispatches a whole batch inside
/// one DI scope: a scoped value would need resetting by hand per message, and a missed reset hands
/// one message's id to the next. <see cref="Begin"/> returns a scope whose disposal restores the
/// previous value, so a <c>using</c> around one dispatch bounds it exactly. It is the same mechanism
/// <c>IHttpContextAccessor</c> is built on.
/// </para>
/// </summary>
public static class AmbientCorrelation
{
    private static readonly AsyncLocal<string?> CurrentValue = new();

    /// <summary>The ambient correlation id, or null when no enclosing operation has set one.</summary>
    public static string? Current => CurrentValue.Value;

    /// <summary>
    /// Makes <paramref name="correlationId"/> ambient until the returned scope is disposed. A null or
    /// blank id leaves the current value in place, so callers can pass whatever they have without
    /// checking it first.
    /// </summary>
    public static IDisposable Begin(string? correlationId)
    {
        var previous = CurrentValue.Value;

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            CurrentValue.Value = correlationId;
        }

        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentValue.Value = previous;
        }
    }
}
