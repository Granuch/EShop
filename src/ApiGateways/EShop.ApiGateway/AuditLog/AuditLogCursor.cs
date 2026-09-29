using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EShop.ApiGateway.AuditLog;

/// <summary>
/// Where the merged audit trail's next page resumes, <b>per service</b>.
///
/// <para>
/// A single cursor cannot work across services. Each service's key is its own identity sequence, so ids from two
/// services cannot be compared; a timestamp cursor would drop or repeat rows whenever two services wrote in the same
/// instant. So the cursor records, for every service, the <c>before</c> value its own next page starts from — which is
/// exactly what that service's keyset paging needs — plus the services already read to the end.
/// </para>
///
/// <para>
/// A service absent from both sets starts from its newest row. The cursor is opaque but not secret: the caller already
/// holds <c>audit.read</c> for every service it names, so a tampered cursor can only page oddly, never widen access.
/// It is validated so that it cannot name an unknown service or a non-positive id.
/// </para>
/// </summary>
public sealed class AuditLogCursor
{
    public static readonly AuditLogCursor Start = new(
        new Dictionary<string, long>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    public AuditLogCursor(IReadOnlyDictionary<string, long> before, IReadOnlySet<string> exhausted)
    {
        Before = before;
        Exhausted = exhausted;
    }

    /// <summary>Service → the id its next page must stay below.</summary>
    public IReadOnlyDictionary<string, long> Before { get; }

    /// <summary>Services whose trail this caller has read to the oldest row.</summary>
    public IReadOnlySet<string> Exhausted { get; }

    public string Encode()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new Wire(
            Before.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
            Exhausted.Order(StringComparer.Ordinal).ToArray()));
        return Base64Url.EncodeToString(json);
    }

    public static bool TryDecode(string? value, IReadOnlyCollection<string> knownServices, out AuditLogCursor cursor)
    {
        cursor = Start;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        Wire? wire;
        try
        {
            wire = JsonSerializer.Deserialize<Wire>(Base64Url.DecodeFromChars(value));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }

        var before = wire?.Before ?? new Dictionary<string, long>();
        var exhausted = wire?.Exhausted ?? [];

        if (before.Any(p => !knownServices.Contains(p.Key) || p.Value <= 0)
            || exhausted.Any(s => !knownServices.Contains(s) || before.ContainsKey(s)))
        {
            return false;
        }

        cursor = new AuditLogCursor(
            new Dictionary<string, long>(before, StringComparer.Ordinal),
            new HashSet<string>(exhausted, StringComparer.Ordinal));
        return true;
    }

    private sealed record Wire(
        [property: JsonPropertyName("b")] Dictionary<string, long>? Before,
        [property: JsonPropertyName("x")] string[]? Exhausted);
}
