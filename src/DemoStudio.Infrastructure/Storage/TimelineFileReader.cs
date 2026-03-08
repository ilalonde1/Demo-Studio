namespace DemoStudio.Infrastructure.Storage;

using System.Text.Json;

public static class TimelineFileReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyCollection<TimelineEntry> LoadFromOutputDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Array.Empty<TimelineEntry>();
        }

        var timelinePath = Path.Combine(outputDirectory, "timeline.json");
        return LoadFromPath(timelinePath);
    }

    public static IReadOnlyCollection<TimelineEntry> LoadFromPath(string timelinePath)
    {
        if (string.IsNullOrWhiteSpace(timelinePath) || !File.Exists(timelinePath))
        {
            return Array.Empty<TimelineEntry>();
        }

        try
        {
            var json = File.ReadAllText(timelinePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<TimelineEntry>();
            }

            var entries = JsonSerializer.Deserialize<List<TimelineEntry>>(json, JsonOptions) ?? new List<TimelineEntry>();
            return entries
                .Where(x => !string.IsNullOrWhiteSpace(x.Stage))
                .OrderBy(x => x.TimestampUtc)
                .ToArray();
        }
        catch
        {
            return Array.Empty<TimelineEntry>();
        }
    }
}

public sealed record TimelineEntry(string Stage, DateTimeOffset TimestampUtc);
