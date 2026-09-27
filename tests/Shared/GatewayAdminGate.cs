using System.Text.Json;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Tests.Shared;

/// <summary>
/// Checks one service's admin endpoints against the gateway's shipped YARP routing table
/// (frontend-contracts F-44, F-45). Linked into each service's integration suite, because only a service knows
/// which of its endpoints are admin-only, and only the gateway's <c>appsettings.json</c> knows which route a path
/// lands on.
///
/// <para>
/// <b>What it pins:</b> every endpoint that requires the <c>Admin</c> role or an EShop permission is either not
/// proxied at all (it matches no YARP route, like the per-service <c>/api/v1/admin/audit</c> the gateway fans out
/// to directly) or lands on a route carrying <see cref="AdminAreaRequirement.PolicyName"/>. Before this, Ordering's
/// and Payment's whole admin surface and Catalog's <c>/categories/{id}/stats</c> were proxied to any caller the
/// storefront route admitted, and nothing noticed, because the service's own check still answered 403.
/// </para>
///
/// <para>
/// Route selection mirrors ASP.NET routing closely enough for this table: the matching route with the lowest
/// <c>Order</c> wins. Two matches at that <c>Order</c> are reported as a violation rather than resolved by
/// template precedence, so an admin route always wins by <c>Order</c>, which is how every admin route in the file
/// is written. Gateway route templates carry no constraints, which <see cref="TemplateMatcher"/> would not
/// evaluate.
/// </para>
/// </summary>
internal static class GatewayAdminGate
{
    /// <summary>The value substituted for every route parameter when building a concrete path.</summary>
    private const string SampleValue = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

    internal sealed record Report(
        IReadOnlyList<string> AdminEndpoints,
        IReadOnlyList<string> Routed,
        IReadOnlyList<string> Violations);

    private sealed record GatewayRoute(string Id, int Order, string? Policy, string[]? Methods, TemplateMatcher Matcher);

    public static Report Check(IServiceProvider services)
    {
        var routes = LoadGatewayRoutes();
        var admin = new List<string>();
        var routed = new List<string>();
        var violations = new List<string>();

        foreach (var endpoint in services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var path = ConcretePath(endpoint.RoutePattern);
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || !IsAdminOnly(endpoint))
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                .Where(m => m != HttpMethods.Head)
                .ToArray() ?? [HttpMethods.Get];

            foreach (var method in methods)
            {
                var name = $"{method} /{endpoint.RoutePattern.RawText?.Trim('/')}";
                admin.Add(name);

                var matches = routes
                    .Where(r => r.Methods is null || r.Methods.Contains(method, StringComparer.OrdinalIgnoreCase))
                    .Where(r => r.Matcher.TryMatch(new PathString(path), new RouteValueDictionary()))
                    .ToList();

                if (matches.Count == 0)
                {
                    continue; // not proxied: the gateway cannot expose it
                }

                var lowest = matches.Min(r => r.Order);
                var winners = matches.Where(r => r.Order == lowest).ToList();
                if (winners.Count > 1)
                {
                    violations.Add($"{name} matches {string.Join(", ", winners.Select(r => r.Id))} at Order {lowest}; "
                        + "give its admin route a lower Order");
                    continue;
                }

                routed.Add(name);
                if (winners[0].Policy != AdminAreaRequirement.PolicyName)
                {
                    violations.Add($"{name} is proxied by '{winners[0].Id}' with policy "
                        + $"'{winners[0].Policy ?? "(anonymous)"}'; add an {AdminAreaRequirement.PolicyName} route for it "
                        + "to src/ApiGateways/EShop.ApiGateway/appsettings.json with a lower Order");
                }
            }
        }

        return new Report(admin, routed, violations);
    }

    /// <summary>The Admin role, by policy name or by <c>[Authorize(Roles = "Admin")]</c>, or any EShop permission.</summary>
    private static bool IsAdminOnly(Endpoint endpoint)
        => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
           && endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a =>
               a.Policy == RolePermissionBundles.AdminRole
               || (a.Policy is not null && EShopPermissions.All.Contains(a.Policy))
               || (a.Roles?.Split(',').Any(r => r.Trim() == RolePermissionBundles.AdminRole) ?? false));

    private static string ConcretePath(RoutePattern pattern)
        => "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => parameter.IsCatchAll ? "x" : SampleValue,
            _ => throw new InvalidOperationException($"unexpected route part {part.GetType().Name}")
        }))));

    private static List<GatewayRoute> LoadGatewayRoutes()
    {
        var file = Path.Combine(RepositoryRoot(), "src", "ApiGateways", "EShop.ApiGateway", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var result = new List<GatewayRoute>();
        foreach (var route in document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").EnumerateObject())
        {
            var match = route.Value.GetProperty("Match");
            var template = match.GetProperty("Path").GetString()!;

            result.Add(new GatewayRoute(
                route.Name,
                route.Value.TryGetProperty("Order", out var order) ? order.GetInt32() : 0,
                route.Value.TryGetProperty("AuthorizationPolicy", out var policy) ? policy.GetString() : null,
                match.TryGetProperty("Methods", out var methods)
                    ? methods.EnumerateArray().Select(m => m.GetString()!).ToArray()
                    : null,
                new TemplateMatcher(TemplateParser.Parse(template.TrimStart('/')), new RouteValueDictionary())));
        }

        return result;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EShop.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("EShop.slnx not found above the test directory.");
    }
}
