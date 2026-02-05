using System.Text.RegularExpressions;

namespace EShop.Identity.IntegrationTests.Helpers;

/// <summary>
/// Helper methods for parsing and verifying Prometheus metrics
/// </summary>
public static class MetricsHelper
{
    public static async Task<string> GetPrometheusMetricsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/metrics");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Parse metric value for a metric with labels
    /// </summary>
    public static double ParseMetric(string metricsContent, string metricName, Dictionary<string, string>? labels = null)
    {
        var lines = metricsContent.Split('\n');

        foreach (var line in lines)
        {
            if (line.StartsWith("#")) continue;
            if (!line.Contains(metricName)) continue;

            // If no labels specified, match any line with the metric name
            if (labels == null || labels.Count == 0)
            {
                var simpleMatch = Regex.Match(line, $@"{Regex.Escape(metricName)}(?:\{{[^}}]*\}})?\s+([\d.]+)");
                if (simpleMatch.Success && double.TryParse(simpleMatch.Groups[1].Value, out var value))
                {
                    return value;
                }
            }
            else
            {
                // Build pattern for labeled metrics
                // Example: identity_login_attempts_total{status="success",reason=""} 5
                var labelPattern = string.Join(",", labels.Select(kvp => 
                    $@"{Regex.Escape(kvp.Key)}=""{Regex.Escape(kvp.Value)}"""));

                var pattern = $@"{Regex.Escape(metricName)}\{{.*{labelPattern}.*\}}\s+([\d.]+)";
                var match = Regex.Match(line, pattern);

                if (match.Success && double.TryParse(match.Groups[1].Value, out var value))
                {
                    return value;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Parse metric value with single label (backward compatibility)
    /// </summary>
    public static double ParseMetric(string metricsContent, string metricName, string? labelValue)
    {
        if (labelValue == null)
        {
            return ParseMetric(metricsContent, metricName, (Dictionary<string, string>?)null);
        }

        // Try common label names: status, result, reason
        var commonLabels = new[] { "status", "result", "reason" };

        foreach (var labelName in commonLabels)
        {
            var labels = new Dictionary<string, string> { { labelName, labelValue } };
            var value = ParseMetric(metricsContent, metricName, labels);
            if (value > 0)
            {
                return value;
            }
        }

        return 0;
    }

    public static bool MetricExists(string metricsContent, string metricName)
    {
        var lines = metricsContent.Split('\n');
        return lines.Any(line => !line.StartsWith("#") && line.Contains(metricName));
    }

    /// <summary>
    /// Get total count for a metric across all label values
    /// </summary>
    public static double GetMetricTotal(string metricsContent, string metricName)
    {
        var lines = metricsContent.Split('\n');
        double total = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("#")) continue;
            if (!line.Contains(metricName)) continue;

            var match = Regex.Match(line, $@"{Regex.Escape(metricName)}(?:\{{[^}}]*\}})?\s+([\d.]+)");
            if (match.Success && double.TryParse(match.Groups[1].Value, out var value))
            {
                total += value;
            }
        }

        return total;
    }
}
