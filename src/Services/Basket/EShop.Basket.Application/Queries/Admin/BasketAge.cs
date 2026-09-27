using System.Globalization;

namespace EShop.Basket.Application.Queries.Admin;

/// <summary>
/// Parses the <c>olderThan</c> of <c>GET /admin/abandoned</c>: a whole number and a unit — <c>90m</c>, <c>24h</c>,
/// <c>3d</c>. Kept to that form on purpose: <see cref="TimeSpan"/>'s own syntax reads <c>"24"</c> as 24 <i>days</i>.
/// </summary>
public static class BasketAge
{
    public const string Default = "24h";

    /// <summary>
    /// Longer than any basket lives (its TTL is 7 days), so every sensible value fits, while still refusing a number
    /// that would overflow the cutoff arithmetic.
    /// </summary>
    public static readonly TimeSpan Max = TimeSpan.FromDays(30);

    public static bool TryParse(string? value, out TimeSpan age)
    {
        age = default;

        if (string.IsNullOrEmpty(value) || value.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0)
        {
            return false;
        }

        var unit = value[^1] switch
        {
            'm' => TimeSpan.FromMinutes(1),
            'h' => TimeSpan.FromHours(1),
            'd' => TimeSpan.FromDays(1),
            _ => TimeSpan.Zero
        };

        // Compared before multiplying: 999999999d would overflow TimeSpan and throw rather than be refused.
        if (unit == TimeSpan.Zero || amount > Max / unit)
        {
            return false;
        }

        age = unit * amount;
        return true;
    }
}
