using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.ApiGateway.Middleware;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Yarp.ReverseProxy.Configuration;

namespace EShop.ApiGateway.AuditLog;

/// <summary>
/// The query string of the gateway's <c>GET /api/v1/admin/audit</c>: a service's filters, plus <see cref="Cursor"/>
/// in place of <c>before</c> and an optional <see cref="Service"/> to read one service only.
/// </summary>
public sealed record GatewayAuditLogRequest
{
    public string? Cursor { get; init; }
    public string? Service { get; init; }
    public int? PageSize { get; init; }
    public string? ActorUserId { get; init; }
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? Outcome { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

/// <summary>
/// One page of the merged audit trail. <see cref="NextCursor"/> is <c>null</c> once every service has been read to its
/// oldest row. <see cref="UnavailableServices"/> names services that could not be read for this page; their rows are
/// not lost — the cursor keeps their position — but may then arrive on a later page, out of time order.
/// </summary>
public sealed record GatewayAuditLogPageDto(
    IReadOnlyList<AuditLogEntryDto> Items,
    string? NextCursor,
    IReadOnlyList<string> UnavailableServices);

/// <summary>
/// The gateway's audit endpoint (admin panel S15, decision Q8a: "the query endpoint fans out across services").
///
/// <para>
/// Every audited service keeps its own <c>audit_log</c> and serves it on the same path; this asks each of them in
/// parallel for one page with the caller's filters and bearer token, and merges the answers (<see cref="AuditLogMerge"/>).
/// The token is forwarded rather than replaced because each service authorizes <c>audit.read</c> itself — the gateway's
/// check is the outer half of defence in depth, not a substitute for the services'.
/// </para>
///
/// <para>
/// <b>A service that fails does not fail the page.</b> An audit screen that goes blank because one service is down is
/// the wrong trade for a read that is used during incidents. The page is returned with that service named in
/// <c>unavailableServices</c>, and its cursor position is carried forward unchanged.
/// </para>
/// </summary>
public sealed class AuditLogFanOut
{
    public const string HttpClientName = "audit-log-fan-out";

    /// <summary>Each audited service and the YARP cluster that reaches it. Basket has no database and no audit trail.</summary>
    public static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["catalog"] = "catalog-cluster",
        ["identity"] = "identity-cluster",
        ["notification"] = "notification-cluster",
        ["ordering"] = "ordering-cluster",
        ["payment"] = "payment-cluster"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProxyConfigProvider _proxyConfig;
    private readonly ILogger<AuditLogFanOut> _logger;

    public AuditLogFanOut(
        IHttpClientFactory httpClientFactory,
        IProxyConfigProvider proxyConfig,
        ILogger<AuditLogFanOut> logger)
    {
        _httpClientFactory = httpClientFactory;
        _proxyConfig = proxyConfig;
        _logger = logger;
    }

    public async Task<IResult> ReadAsync(GatewayAuditLogRequest request, HttpContext httpContext)
    {
        var shared = new AuditLogRequest
        {
            PageSize = request.PageSize,
            ActorUserId = request.ActorUserId,
            Action = request.Action,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Outcome = request.Outcome,
            From = request.From,
            To = request.To
        };

        if (!AuditLogQueryRules.TryCreateFilter(shared, out var filter, out var error))
        {
            return Invalid(error!);
        }

        if (!AuditLogCursor.TryDecode(request.Cursor, Sources.Keys.ToList(), out var cursor))
        {
            return Invalid("'cursor' is not a cursor this endpoint issued.");
        }

        var service = string.IsNullOrWhiteSpace(request.Service) ? null : request.Service.Trim().ToLowerInvariant();
        if (service is not null && !Sources.ContainsKey(service))
        {
            return Invalid($"'service' must be one of: {string.Join(", ", Sources.Keys)}.");
        }

        var targets = Sources.Keys
            .Where(s => service is null || s == service)
            .Where(s => !cursor.Exhausted.Contains(s))
            .ToList();

        var fetched = await Task.WhenAll(targets.Select(async s =>
            (Service: s, Page: await FetchAsync(s, filter, cursor.Before.GetValueOrDefault(s), httpContext))));

        var pages = fetched.ToDictionary(f => f.Service, f => f.Page, StringComparer.Ordinal);
        var (items, next) = AuditLogMerge.Merge(filter.PageSize, cursor, pages);

        var unavailable = fetched.Where(f => f.Page is null).Select(f => f.Service).ToList();
        var finished = targets.All(next.Exhausted.Contains);

        return Results.Ok(new GatewayAuditLogPageDto(items, finished ? null : next.Encode(), unavailable));
    }

    private async Task<AuditLogPageDto?> FetchAsync(
        string service,
        AuditLogFilter filter,
        long before,
        HttpContext httpContext)
    {
        var address = _proxyConfig.GetConfig().Clusters
            .FirstOrDefault(c => c.ClusterId == Sources[service])?
            .Destinations?.Values.FirstOrDefault()?.Address;

        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var baseUri))
        {
            _logger.LogWarning("Audit fan-out: no usable destination for {Service} ({ClusterId})", service, Sources[service]);
            return null;
        }

        var query = new QueryBuilder { { "pageSize", filter.PageSize.ToString(CultureInfo.InvariantCulture) } };
        if (before > 0) query.Add("before", before.ToString(CultureInfo.InvariantCulture));
        if (filter.ActorUserId is { } actor) query.Add("actorUserId", actor);
        if (filter.Action is { } action) query.Add("action", action);
        if (filter.EntityType is { } entityType) query.Add("entityType", entityType);
        if (filter.EntityId is { } entityId) query.Add("entityId", entityId);
        if (filter.Outcome is { } outcome) query.Add("outcome", outcome.ToString());
        if (filter.From is { } from) query.Add("from", from.ToString("O", CultureInfo.InvariantCulture));
        if (filter.To is { } to) query.Add("to", to.ToString("O", CultureInfo.InvariantCulture));

        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri, AuditLogEndpoints.Path.TrimStart('/') + query.ToQueryString()));

        if (AuthenticationHeaderValue.TryParse(httpContext.Request.Headers.Authorization, out var authorization))
        {
            message.Headers.Authorization = authorization;
        }

        if (httpContext.Items[CorrelationIdMiddleware.CorrelationItemKey] is string correlationId)
        {
            message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        }

        try
        {
            using var response = await _httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(message, httpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Audit fan-out: {Service} answered {StatusCode}", service, (int)response.StatusCode);
                return null;
            }

            var page = await response.Content.ReadFromJsonAsync<AuditLogPageDto>(httpContext.RequestAborted);
            if (page?.Items is null)
            {
                _logger.LogWarning("Audit fan-out: {Service} returned no page", service);
                return null;
            }

            return page;
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or NotSupportedException)
        {
            _logger.LogWarning(ex, "Audit fan-out: {Service} could not be read", service);
            return null;
        }
    }

    private static IResult Invalid(string detail)
        => ProblemResults.For(AuditLogQueryRules.InvalidQueryCode, detail, StatusCodes.Status400BadRequest);
}

public static class AuditLogFanOutEndpoints
{
    /// <summary>
    /// Maps the gateway's own <c>GET /api/v1/admin/audit</c>. It is an endpoint, not a YARP route, so it does not appear
    /// in the routing-table tests; its policy is pinned by <c>AuditLogFanOutTests</c> instead.
    /// </summary>
    public static IEndpointRouteBuilder MapGatewayAuditLog(this IEndpointRouteBuilder app)
    {
        app.MapGet(AuditLogEndpoints.Path, (
                [AsParameters] GatewayAuditLogRequest request,
                HttpContext httpContext,
                AuditLogFanOut fanOut) => fanOut.ReadAsync(request, httpContext))
            .RequireAuthorization(EShopPermissions.AuditRead)
            .WithName("GetMergedAuditLog")
            .WithTags("Audit")
            .Produces<GatewayAuditLogPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
