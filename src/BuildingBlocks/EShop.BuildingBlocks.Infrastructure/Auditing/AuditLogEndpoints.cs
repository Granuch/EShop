using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EShop.BuildingBlocks.Infrastructure.Auditing;

/// <summary>
/// The query-string surface of <c>GET /api/v1/admin/audit</c>, on a service and (with a cursor instead of
/// <see cref="Before"/>) on the gateway. Every value-typed property is nullable, or a request omitting it would fail
/// binding with a 400 about the request <i>body</i> (the root guide's <c>[AsParameters]</c> trap).
/// </summary>
public sealed record AuditLogRequest
{
    public long? Before { get; init; }
    public int? PageSize { get; init; }
    public string? ActorUserId { get; init; }
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? Outcome { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

/// <summary>The rules for an audit query, shared by every service and by the gateway's fan-out.</summary>
public static class AuditLogQueryRules
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public const string InvalidQueryCode = "Validation.Failed";

    /// <summary>
    /// Validates <paramref name="request"/> into a filter, or explains why not. Only <see cref="AuditLogRequest.Before"/>
    /// is service-specific; everything else is checked identically wherever the query arrives.
    /// </summary>
    public static bool TryCreateFilter(AuditLogRequest request, out AuditLogFilter filter, out string? error)
    {
        filter = null!;

        if (request.Before is <= 0)
        {
            error = "'before' must be a positive audit entry id.";
            return false;
        }

        if (!TryValidateShared(request, out var pageSize, out var outcome, out var from, out var to, out error))
        {
            return false;
        }

        filter = new AuditLogFilter(
            request.Before,
            pageSize,
            Blank(request.ActorUserId),
            Blank(request.Action),
            Blank(request.EntityType),
            Blank(request.EntityId),
            outcome,
            from,
            to);
        return true;
    }

    /// <summary>Everything but <see cref="AuditLogRequest.Before"/>: page size, outcome, dates and field lengths.</summary>
    public static bool TryValidateShared(
        AuditLogRequest request,
        out int pageSize,
        out AuditOutcome? outcome,
        out DateTime? from,
        out DateTime? to,
        out string? error)
    {
        pageSize = request.PageSize ?? DefaultPageSize;
        outcome = null;
        from = AsUtc(request.From);
        to = AsUtc(request.To);

        if (pageSize is < 1 or > MaxPageSize)
        {
            error = $"'pageSize' must be between 1 and {MaxPageSize}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Outcome))
        {
            // Names only: Enum.TryParse would also accept "7", and an out-of-range number would match nothing silently.
            var name = Enum.GetNames<AuditOutcome>()
                .FirstOrDefault(n => string.Equals(n, request.Outcome.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                error = $"'outcome' must be one of: {string.Join(", ", Enum.GetNames<AuditOutcome>())}.";
                return false;
            }

            outcome = Enum.Parse<AuditOutcome>(name);
        }

        if (from is { } f && to is { } t && f >= t)
        {
            error = "'from' must be earlier than 'to'.";
            return false;
        }

        if (TooLong(request.ActorUserId, AuditLogEntry.ActorUserIdMaxLength, "actorUserId", out error)
            || TooLong(request.Action, AuditLogEntry.ActionMaxLength, "action", out error)
            || TooLong(request.EntityType, AuditLogEntry.EntityTypeMaxLength, "entityType", out error)
            || TooLong(request.EntityId, AuditLogEntry.EntityIdMaxLength, "entityId", out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// A query-string date arrives as <c>Kind.Unspecified</c> when it has no zone, and Npgsql refuses to send one as a
    /// <c>timestamp with time zone</c> — so without this, <c>?from=2026-09-01</c> is a 500 rather than a filter.
    /// Unspecified is read as UTC, Local is converted.
    /// </summary>
    public static DateTime? AsUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { } v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    };

    private static bool TooLong(string? value, int max, string name, out string? error)
    {
        error = value is not null && value.Length > max ? $"'{name}' must be at most {max} characters." : null;
        return error is not null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class AuditLogEndpoints
{
    /// <summary>The path a service serves its own audit trail on — and the path the gateway serves the merged one on.</summary>
    public const string Path = "/api/v1/admin/audit";

    /// <summary>
    /// Maps <c>GET /api/v1/admin/audit</c> — this service's slice of the audit trail, under <c>audit.read</c>. The
    /// gateway serves the same path itself and fans out to every service's (decision Q8a); no YARP route proxies it,
    /// so a client never reaches this one directly, and it authorizes on its own all the same.
    /// </summary>
    public static IEndpointRouteBuilder MapEShopAuditLog(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, async (
                [AsParameters] AuditLogRequest request,
                IAuditLogReader reader,
                CancellationToken cancellationToken) =>
            {
                if (!AuditLogQueryRules.TryCreateFilter(request, out var filter, out var error))
                {
                    return ProblemResults.For(AuditLogQueryRules.InvalidQueryCode, error!, StatusCodes.Status400BadRequest);
                }

                return Results.Ok(await reader.ReadAsync(filter, cancellationToken));
            })
            .RequireAuthorization(EShopPermissions.AuditRead)
            .WithName("GetAuditLog")
            .WithTags("Audit")
            .Produces<AuditLogPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
