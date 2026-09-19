using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// Reads <c>AdminUsersController</c>'s routing table out of a running host, so the structural
/// authorization tests in this namespace agree on what "an admin-users endpoint" is.
/// </summary>
/// <remarks>
/// Shared between the read and write authorization fixtures because they partition the same set by
/// verb: if the two disagreed about which endpoints exist, an action could fall into the gap and be
/// asserted by neither — which is exactly the hole these tests exist to close.
/// </remarks>
internal static class AdminUserEndpoints
{
    private const string Prefix = "api/v1/admin/users";

    public static IReadOnlyList<RouteEndpoint> Of(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/')
                .StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

    /// <summary>
    /// A read is a GET (or HEAD, which ASP.NET adds alongside it). Anything else is a write.
    /// </summary>
    public static bool IsRead(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        return methods is not null
            && methods.All(m => m is "GET" or "HEAD" or "OPTIONS");
    }

    public static bool IsWrite(RouteEndpoint endpoint) => !IsRead(endpoint);

    public static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return $"{string.Join("/", methods)} {endpoint.RoutePattern.RawText}";
    }
}
