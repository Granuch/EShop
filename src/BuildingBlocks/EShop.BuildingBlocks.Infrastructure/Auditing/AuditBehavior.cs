using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.BuildingBlocks.Infrastructure.Auditing;

/// <summary>The service name every audit row of this process carries (<c>catalog</c>, <c>identity</c>, …).</summary>
public sealed record AuditLogOptions(string ServiceName);

/// <summary>
/// Writes one <c>audit_log</c> row per execution of an <see cref="IAuditedCommand"/> (admin panel decision Q8a) — or, for
/// a batch command that ran, one row per item it acted on (<see cref="IAuditedCommand.AuditItemsFromResult"/>, S16).
///
/// <para>
/// <b>It must be the OUTERMOST behavior</b> — registered before <c>AddEShopCacheInvalidation()</c> and before
/// <c>Add&lt;Service&gt;Application()</c>, which is what <c>AddEShopAuditLog</c>'s position in each <c>Program.cs</c>
/// achieves. Outermost means it runs after <c>TransactionBehavior</c> has committed or rolled back, so it records the
/// outcome the caller actually got, and it sees validation failures too. Each service pins the position in a test.
/// </para>
///
/// <para>
/// <b>Every outcome is recorded, not only success.</b> A <c>Result</c> failure is <see cref="AuditOutcome.Rejected"/>
/// with its error code, and a thrown exception is <see cref="AuditOutcome.Failed"/> with the exception's <i>type
/// name</i> — never its message, which can carry SQL, constraint names or hosts into a table an operator reads. A
/// rejected admin action is exactly what an audit trail is for, and "rejected" does not even mean "nothing changed":
/// <c>TransactionBehavior</c> commits on any non-exception return. Authorization failures never reach MediatR, so a 403
/// is not recorded here.
/// </para>
///
/// <para>
/// <b>A failed audit write never fails the command.</b> By the time the row is written the command has committed; an
/// exception here would turn a successful mutation into a 500, and a client that retries would apply it twice. The
/// failure is logged at Error with the row's identifying fields instead, so the event survives in the logs.
/// </para>
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private const string CommandSuffix = "Command";

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    // Result<T> and Result share no base type, so the generic one is read through its properties. TResponse is fixed
    // per closed behavior type, so this is computed once per command type.
    private static readonly GenericResultAccessor? GenericResult = GenericResultAccessor.For(typeof(TResponse));

    private readonly IAuditLogWriter _writer;
    private readonly ICurrentUserContext _currentUser;
    private readonly AuditLogOptions _options;
    private readonly ILogger<AuditBehavior<TRequest, TResponse>> _logger;

    public AuditBehavior(
        IAuditLogWriter writer,
        ICurrentUserContext currentUser,
        AuditLogOptions options,
        ILogger<AuditBehavior<TRequest, TResponse>> logger)
    {
        _writer = writer;
        _currentUser = currentUser;
        _options = options;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IAuditedCommand audited)
        {
            return await next();
        }

        TResponse response;
        try
        {
            response = await next();
        }
        catch (Exception ex)
        {
            await RecordAsync(request, audited, AuditOutcome.Failed, ex.GetType().Name, entityId: audited.AuditEntityId);
            throw;
        }

        var (outcome, errorCode, value) = Inspect(response);

        // Admin panel S16. A batch that ran is recorded per item, so each product it touched can be found by its own id.
        // Only when it ran: a batch refused as a whole acted on nothing, and falls through to the one row below.
        if (outcome == AuditOutcome.Succeeded
            && audited.AuditItemsFromResult(value) is { Count: > 0 } items)
        {
            await RecordItemsAsync(audited, items);
            return response;
        }

        var entityId = audited.AuditEntityId
            ?? (outcome == AuditOutcome.Succeeded ? audited.AuditEntityIdFromResult(value) : null);

        await RecordAsync(request, audited, outcome, errorCode, entityId);
        return response;
    }

    private async Task RecordItemsAsync(IAuditedCommand audited, IReadOnlyList<AuditedItem> items)
    {
        var action = ActionName();

        try
        {
            // One instant for the whole batch: the items were committed together, and a reader sorting by time should
            // not see a batch interleaved with whatever else happened while its rows were being built.
            var occurredAt = DateTime.UtcNow;

            var entries = items
                .Select(item => AuditLogEntry.Record(
                    occurredAt: occurredAt,
                    service: _options.ServiceName,
                    action: action,
                    entityType: audited.AuditEntityType,
                    entityId: item.EntityId,
                    actorUserId: _currentUser.UserId,
                    actorName: _currentUser.UserName,
                    correlationId: _currentUser.CorrelationId,
                    outcome: item.ErrorCode is null ? AuditOutcome.Succeeded : AuditOutcome.Rejected,
                    errorCode: item.ErrorCode,
                    payloadJson: item.Detail is null
                        ? null
                        : JsonSerializer.Serialize(SafeRequestRenderer.Render(item.Detail), PayloadJsonOptions)))
                .ToList();

            await _writer.WriteAllAsync(entries);
        }
        catch (Exception ex)
        {
            // Same posture as a single row: the batch has committed, so failing it now would invite a retry that applies
            // it twice.
            _logger.LogError(
                ex,
                "Failed to write the {Count} audit records for batch {Action} on {EntityType} by {ActorUserId}; "
                + "the command's own outcome is unaffected",
                items.Count, action, audited.AuditEntityType, _currentUser.UserId);
        }
    }

    private async Task RecordAsync(
        TRequest request,
        IAuditedCommand audited,
        AuditOutcome outcome,
        string? errorCode,
        string? entityId)
    {
        var action = ActionName();

        try
        {
            var entry = AuditLogEntry.Record(
                occurredAt: DateTime.UtcNow,
                service: _options.ServiceName,
                action: action,
                entityType: audited.AuditEntityType,
                entityId: entityId,
                actorUserId: _currentUser.UserId,
                actorName: _currentUser.UserName,
                correlationId: _currentUser.CorrelationId,
                outcome: outcome,
                errorCode: errorCode,
                payloadJson: JsonSerializer.Serialize(SafeRequestRenderer.Render(request), PayloadJsonOptions));

            await _writer.WriteAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to write the audit record for {Action} on {EntityType} {EntityId} by {ActorUserId} ({Outcome}); "
                + "the command's own outcome is unaffected",
                action, audited.AuditEntityType, entityId, _currentUser.UserId, outcome);
        }
    }

    private static string ActionName()
    {
        var name = typeof(TRequest).Name;
        return name.EndsWith(CommandSuffix, StringComparison.Ordinal) && name.Length > CommandSuffix.Length
            ? name[..^CommandSuffix.Length]
            : name;
    }

    private static (AuditOutcome Outcome, string? ErrorCode, object? Value) Inspect(TResponse response)
    {
        if (response is Result result)
        {
            return result.IsSuccess
                ? (AuditOutcome.Succeeded, null, null)
                : (AuditOutcome.Rejected, result.Error?.Code, null);
        }

        if (GenericResult is not null && response is not null)
        {
            return GenericResult.IsSuccess(response)
                ? (AuditOutcome.Succeeded, null, GenericResult.Value(response))
                : (AuditOutcome.Rejected, GenericResult.Error(response)?.Code, null);
        }

        // Not a Result at all: the command returned normally, which is success as far as its caller can tell.
        return (AuditOutcome.Succeeded, null, response);
    }

    private sealed class GenericResultAccessor
    {
        private readonly PropertyInfo _isSuccess;
        private readonly PropertyInfo _value;
        private readonly PropertyInfo _error;

        private GenericResultAccessor(Type type)
        {
            _isSuccess = type.GetProperty(nameof(Result<object>.IsSuccess))!;
            _value = type.GetProperty(nameof(Result<object>.Value))!;
            _error = type.GetProperty(nameof(Result<object>.Error))!;
        }

        public static GenericResultAccessor? For(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>)
                ? new GenericResultAccessor(type)
                : null;

        public bool IsSuccess(object result) => (bool)_isSuccess.GetValue(result)!;

        public object? Value(object result) => _value.GetValue(result);

        public Error? Error(object result) => (Error?)_error.GetValue(result);
    }
}
