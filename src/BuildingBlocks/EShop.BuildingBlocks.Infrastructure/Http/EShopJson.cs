using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// The JSON conventions every HTTP surface shares (frontend-contracts F-01): <b>an enum is its PascalCase name</b>, in
/// a response and in a request body.
/// </summary>
/// <remarks>
/// <para>
/// System.Text.Json writes a bare enum as a number, so before this Catalog sent <c>"status": 1</c> and Ordering
/// <c>"status": 0</c> while Payment, Notification and the audit log sent names — a client needed a lookup table per
/// service and broke silently whenever a member was inserted mid-enum.
/// </para>
/// <para>
/// <b>Integers are refused on input</b> (<c>allowIntegerValues: false</c>), for the same reason the query-string
/// validators refuse them: <c>7</c> or <c>-1</c> would bind as a value no row can have. Names are read in any case,
/// which is System.Text.Json's behaviour for this converter.
/// </para>
/// <para>
/// HTTP only. Integration events, the outbox and the distributed cache each keep their own serializer options, so
/// registering this changes no message contract and no cached entry.
/// </para>
/// </remarks>
public static class EShopJson
{
    public static JsonSerializerOptions UseEShopConventions(this JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    /// <summary>Minimal-API endpoints and <c>Results.Ok(...)</c>.</summary>
    public static IServiceCollection AddEShopJson(this IServiceCollection services)
        => services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UseEShopConventions());

    /// <summary>MVC controllers, which read their own <c>JsonOptions</c>, not the minimal-API ones.</summary>
    public static IMvcBuilder AddEShopJson(this IMvcBuilder builder)
        => builder.AddJsonOptions(options => options.JsonSerializerOptions.UseEShopConventions());
}
