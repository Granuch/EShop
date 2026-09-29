using EShop.BuildingBlocks.Domain;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace EShop.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Renders a request object into a shape that is safe to write down: a tree of dictionaries, lists and primitives in
/// which every secret-bearing or personal-data property reads <c>****</c>.
///
/// <para>
/// A property is redacted when it carries <see cref="SensitiveDataAttribute"/> <b>or</b> its name is on
/// <see cref="RedactedPropertyNames"/>. The attribute is the contract; the name list is a coincidence — renaming
/// <c>Code</c> to <c>OtpCode</c> silently stops redacting it — so mark new secret- or PII-bearing properties with the
/// attribute rather than relying on the name.
/// </para>
///
/// <para>
/// Two writers share this on purpose: <see cref="LoggingBehavior{TRequest,TResponse}"/>, which logs every request at
/// Information, and the audit behavior, which stores an admin command's payload in <c>audit_log</c>. One renderer
/// means a property redacted in the logs is redacted in the audit trail too, and the other way round — two copies of
/// the rules would drift, and the drift would be a secret at rest.
/// </para>
///
/// <para>
/// Only <b>public instance</b> properties are read, so an explicit interface implementation — how commands declare
/// their <c>IAuditedCommand</c> members — never appears in the output. Collections are cut after
/// <see cref="MaxCollectionItems"/> items and nesting after <see cref="MaxDepth"/> levels.
/// </para>
/// </summary>
public static class SafeRequestRenderer
{
    public const int MaxDepth = 4;
    public const int MaxCollectionItems = 25;

    /// <summary>The value written in place of a redacted property.</summary>
    public const string Redacted = "****";

    private static readonly HashSet<string> RedactedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password",
        "NewPassword",
        "CurrentPassword",
        "Token",
        "RefreshToken",
        "AccessToken",
        "Secret",
        "SecretKey",
        "ApiKey",
        "TwoFactorCode",
        "Code",
        "Otp",
        "Passcode",
        "ClientSecret",
        "ClientSecretValue",
        "SecurityToken",
        "AuthorizationCode",
        "RecoveryCode",
        "Pin"
    };

    private static readonly ConcurrentDictionary<Type, SafeLogPropertyMetadata[]> MetadataCache = new();

    /// <summary>Renders <paramref name="obj"/>; see the type's remarks for what is redacted and cut.</summary>
    public static object? Render(object? obj) => Render(obj, depth: 0);

    private static object? Render(object? obj, int depth)
    {
        if (obj == null)
            return null;

        if (depth >= MaxDepth)
            return "[MaxDepthReached]";

        if (obj is ISafeLoggable loggable)
            return loggable.ToSafeLog();

        var type = obj.GetType();

        if (type.IsPrimitive ||
            obj is string ||
            obj is decimal ||
            obj is DateTime ||
            obj is DateTimeOffset ||
            obj is Guid ||
            obj is TimeSpan ||
            type.IsEnum)
            return obj;

        if (obj is SecretString)
            return Redacted;

        if (obj is IEnumerable enumerable)
        {
            var items = new List<object?>();
            var count = 0;
            foreach (var item in enumerable)
            {
                if (count >= MaxCollectionItems)
                {
                    items.Add($"[TruncatedAfter:{MaxCollectionItems}]");
                    break;
                }

                items.Add(Render(item, depth + 1));
                count++;
            }

            return items;
        }

        var metadata = MetadataCache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod is not null)
                .Select(p => new SafeLogPropertyMetadata(
                    p,
                    p.GetCustomAttribute<SensitiveDataAttribute>() != null || RedactedPropertyNames.Contains(p.Name)))
                .ToArray());

        var dict = new Dictionary<string, object?>(metadata.Length, StringComparer.Ordinal);
        foreach (var item in metadata)
        {
            object? value;
            try
            {
                value = item.Property.GetValue(obj);
            }
            catch
            {
                value = "[ReadError]";
            }

            dict[item.Property.Name] = item.IsSensitive ? Redacted : Render(value, depth + 1);
        }

        return dict;
    }

    private sealed record SafeLogPropertyMetadata(PropertyInfo Property, bool IsSensitive);
}
