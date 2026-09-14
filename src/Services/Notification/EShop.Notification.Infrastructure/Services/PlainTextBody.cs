using System.Net;
using System.Text.RegularExpressions;

namespace EShop.Notification.Infrastructure.Services;

/// <summary>
/// Notification audit S7 (L21). The plain-text alternative of a rendered template: the emails were HTML only, which some
/// clients cannot show and spam filters count against. Blocks become lines, table cells stay on their row, a link keeps
/// its target in brackets (a <c>mailto:</c> link is just its address), and entities are decoded.
/// </summary>
public static partial class PlainTextBody
{
    public static string FromHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var text = Head().Replace(html, string.Empty);
        text = Whitespace().Replace(text, " ");
        text = Anchor().Replace(text, match =>
        {
            var href = match.Groups["href"].Value;
            var label = Tag().Replace(match.Groups["label"].Value, string.Empty).Trim();
            return href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href == label
                ? label
                : $"{label} ({href})";
        });
        text = BlockEnd().Replace(text, "\n");
        text = CellEnd().Replace(text, " ");
        text = Tag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var lines = text.Split('\n').Select(line => Spaces().Replace(line, " ").Trim());
        return BlankLines().Replace(string.Join("\n", lines), "\n\n").Trim();
    }

    [GeneratedRegex(@"<head\b.*?</head>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Head();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<a\b[^>]*?\bhref=""(?<href>[^""]*)""[^>]*>(?<label>.*?)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"<br\s*/?>|</(?:p|h1|h2|h3|tr|div|table)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"</td>", RegexOptions.IgnoreCase)]
    private static partial Regex CellEnd();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
