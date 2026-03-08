using System.Globalization;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopPerformanceMetricsService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, List<double>> _samples = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxSamplesPerOperation;

    public DesktopPerformanceMetricsService(int maxSamplesPerOperation = 200)
    {
        _maxSamplesPerOperation = Math.Max(20, maxSamplesPerOperation);
    }

    public string Record(string operationName, TimeSpan elapsed)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            operationName = "Unknown";
        }

        var ms = Math.Max(0d, elapsed.TotalMilliseconds);
        lock (_sync)
        {
            if (!_samples.TryGetValue(operationName, out var list))
            {
                list = new List<double>(_maxSamplesPerOperation);
                _samples[operationName] = list;
            }

            list.Add(ms);
            if (list.Count > _maxSamplesPerOperation)
            {
                list.RemoveAt(0);
            }

            return BuildSummaryUnsafe();
        }
    }

    private string BuildSummaryUnsafe()
    {
        if (_samples.Count == 0)
        {
            return "Perf: no samples yet.";
        }

        var segments = new List<string>();
        foreach (var item in _samples.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (item.Value.Count == 0)
            {
                continue;
            }

            var ordered = item.Value.OrderBy(x => x).ToArray();
            var p50 = Percentile(ordered, 0.50);
            var p95 = Percentile(ordered, 0.95);
            segments.Add($"{item.Key} p50 {FormatMs(p50)} p95 {FormatMs(p95)}");
        }

        return segments.Count == 0 ? "Perf: no samples yet." : string.Join(" | ", segments);
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0d;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var position = percentile * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var weight = position - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * weight;
    }

    private static string FormatMs(double ms)
    {
        if (ms >= 1000d)
        {
            return (ms / 1000d).ToString("0.##s", CultureInfo.InvariantCulture);
        }

        return ms.ToString("0ms", CultureInfo.InvariantCulture);
    }
}
