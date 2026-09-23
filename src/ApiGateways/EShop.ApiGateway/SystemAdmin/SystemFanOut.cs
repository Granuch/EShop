using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.ApiGateway.Middleware;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Configuration;

namespace EShop.ApiGateway.SystemAdmin;

/// <summary>A service behind the gateway and the YARP cluster that reaches it.</summary>
public sealed record SystemSource(string Name, string ClusterId);

/// <summary>
/// The System page's calls to the services behind the gateway (admin panel S19): each service's read-only settings or
/// feature flags with the caller's token, and each service's anonymous <c>/health</c>.
///
/// <para>
/// <b>A service that cannot be read never fails the page</b> — the audit trail's rule (S15, decision 7), for the same
/// reason: this is the screen an operator opens during an incident. A settings or flags slice comes back <c>null</c>
/// and the service is named as unavailable; a health probe comes back as an unreachable, unhealthy component.
/// </para>
///
/// <para>
/// Kept apart from <c>AuditLogFanOut</c> rather than shared with it: the audit client has its own name, timeout and
/// faked handler in its tests, and merging them would change a tested component to save thirty lines.
/// </para>
/// </summary>
public sealed class SystemFanOut
{
    public const string HttpClientName = "system-fan-out";

    /// <summary>
    /// Every service behind the gateway, in the order the health page lists them. <c>SystemEndpointsTests</c> pins this
    /// against the real cluster list in both directions, so a new cluster cannot be missing from the health page and a
    /// renamed one cannot silently read as "unreachable" forever.
    /// </summary>
    public static readonly IReadOnlyList<SystemSource> Sources =
    [
        new("identity", "identity-cluster"),
        new("catalog", "catalog-cluster"),
        new("basket", "basket-cluster"),
        new("ordering", "ordering-cluster"),
        new("payment", "payment-cluster"),
        new("notification", "notification-cluster")
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProxyConfigProvider _proxyConfig;
    private readonly ILogger<SystemFanOut> _logger;

    public SystemFanOut(
        IHttpClientFactory httpClientFactory,
        IProxyConfigProvider proxyConfig,
        ILogger<SystemFanOut> logger)
    {
        _httpClientFactory = httpClientFactory;
        _proxyConfig = proxyConfig;
        _logger = logger;
    }

    /// <summary>
    /// GETs <paramref name="path"/> from <paramref name="service"/> as the caller — the service authorizes the request
    /// itself — and reads the body as <typeparamref name="T"/>. <c>null</c> for anything but a readable 2xx.
    /// </summary>
    public async Task<T?> GetAsCallerAsync<T>(string service, string path, HttpContext httpContext) where T : class
    {
        var baseUri = BaseAddress(service);
        if (baseUri is null)
        {
            return null;
        }

        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path.TrimStart('/')));

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
            using var response = await Client.SendAsync(message, httpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("System page: {Service} answered {StatusCode} for {Path}", service, (int)response.StatusCode, path);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            _logger.LogWarning(ex, "System page: {Service} could not be read for {Path}", service, path);
            return null;
        }
    }

    /// <summary>
    /// Reads <paramref name="service"/>'s own <c>/health</c> — every check, not just readiness. Anonymous on purpose: the
    /// endpoint is, and forwarding an administrator's token to it would spread the token for nothing.
    /// </summary>
    public async Task<ComponentHealthDto> GetHealthAsync(string service, CancellationToken cancellationToken)
    {
        var baseUri = BaseAddress(service);
        if (baseUri is null)
        {
            return ComponentHealthDto.Unreachable(service);
        }

        HttpResponseMessage response;
        try
        {
            // A 503 is an answer, not a failure: the health middleware writes the same body with Unhealthy in it.
            response = await Client.GetAsync(new Uri(baseUri, "health"), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            _logger.LogWarning(ex, "System page: {Service} health could not be reached", service);
            return ComponentHealthDto.Unreachable(service);
        }

        // Two steps on purpose: once a response has arrived the service WAS reached, so a body that fails to parse (a
        // 404 page, a proxy's error page) must not read as "unreachable" — that sends an operator looking at the network.
        using (response)
        {
            var body = await TryReadHealthBodyAsync(response, cancellationToken);

            if (body is null || !IsHealthStatusName(body.Status))
            {
                // Reached, but not our health body: something answers, and it is not a healthy service. Anything the body
                // said is dropped rather than repeated.
                _logger.LogWarning("System page: {Service} answered {StatusCode} with no readable health body",
                    service, (int)response.StatusCode);
                return new ComponentHealthDto(service, Reachable: true, nameof(HealthStatus.Unhealthy), []);
            }

            // Re-projected, never passed through: the page must stay as information-free as the endpoint it reads
            // (SEC-07), and a check status that is not a HealthStatus name is reported as Unhealthy rather than echoed.
            var checks = body.Checks
                .Select(c => new EShopHealthCheckEntry
                {
                    Name = c.Name,
                    Status = IsHealthStatusName(c.Status) ? c.Status : nameof(HealthStatus.Unhealthy)
                })
                .ToList();

            return new ComponentHealthDto(service, Reachable: true, body.Status, checks);
        }
    }

    private async Task<EShopHealthResponse?> TryReadHealthBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<EShopHealthResponse>(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            _logger.LogDebug(ex, "System page: a health body could not be parsed");
            return null;
        }
    }

    private HttpClient Client => _httpClientFactory.CreateClient(HttpClientName);

    private Uri? BaseAddress(string service)
    {
        var clusterId = Sources.Single(s => s.Name == service).ClusterId;
        var address = _proxyConfig.GetConfig().Clusters
            .FirstOrDefault(c => c.ClusterId == clusterId)?
            .Destinations?.Values.FirstOrDefault()?.Address;

        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var baseUri))
        {
            _logger.LogWarning("System page: no usable destination for {Service} ({ClusterId})", service, clusterId);
            return null;
        }

        return baseUri;
    }

    /// <summary>
    /// Exactly a <see cref="HealthStatus"/> name. Not <c>Enum.TryParse</c>, which also accepts <c>"5"</c> and any other
    /// number — an undefined status that would then be echoed onto the page and break the worst-status comparison.
    /// </summary>
    public static bool IsHealthStatusName(string? value)
        => value is not null && Enum.GetNames<HealthStatus>().Contains(value, StringComparer.Ordinal);

    private static bool IsReadFailure(Exception ex)
        => ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException;
}
