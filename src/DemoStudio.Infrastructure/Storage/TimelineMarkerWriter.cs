namespace DemoStudio.Infrastructure.Storage;

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DemoStudio.Application.Abstractions.System;

public sealed class TimelineMarkerWriter : ITimelineMarkerWriter
{
    private const string TimelineFileName = "timeline.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IFileStorage _fileStorage;
    private readonly ConcurrentDictionary<string, FileGate> _fileLocks = new(StringComparer.OrdinalIgnoreCase);

    public TimelineMarkerWriter(IFileStorage fileStorage)
    {
        _fileStorage = fileStorage;
    }

    public async Task WriteStageMarkerAsync(string outputDirectory, string stage, DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        if (string.IsNullOrWhiteSpace(stage))
        {
            throw new ArgumentException("Stage is required.", nameof(stage));
        }

        var timelinePath = Path.Combine(outputDirectory, TimelineFileName);
        var fileGate = _fileLocks.GetOrAdd(timelinePath, _ => new FileGate());
        fileGate.AddRef();
        var gateAcquired = false;
        try
        {
            await fileGate.Gate.WaitAsync(cancellationToken);
            gateAcquired = true;

            var markers = await LoadMarkersAsync(timelinePath, cancellationToken);
            markers.Add(new TimelineMarkerEntry(stage.Trim(), timestampUtc.UtcDateTime));

            var payload = JsonSerializer.Serialize(markers, SerializerOptions);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            await _fileStorage.SaveAsync(timelinePath, stream, cancellationToken);
        }
        finally
        {
            if (gateAcquired)
            {
                fileGate.Gate.Release();
            }

            if (fileGate.ReleaseRef() == 0 && _fileLocks.TryRemove(timelinePath, out var removed))
            {
                removed.Dispose();
            }
        }
    }

    private async Task<List<TimelineMarkerEntry>> LoadMarkersAsync(string timelinePath, CancellationToken cancellationToken)
    {
        var exists = await _fileStorage.ExistsAsync(timelinePath, cancellationToken);
        if (!exists)
        {
            return new List<TimelineMarkerEntry>();
        }

        var payload = await _fileStorage.ReadAllTextAsync(timelinePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new List<TimelineMarkerEntry>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<TimelineMarkerEntry>>(payload, SerializerOptions) ?? new List<TimelineMarkerEntry>();
        }
        catch (JsonException)
        {
            return new List<TimelineMarkerEntry>();
        }
    }

    private sealed record TimelineMarkerEntry(string Stage, DateTime TimestampUtc);

    private sealed class FileGate : IDisposable
    {
        private int _refCount;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void AddRef()
        {
            Interlocked.Increment(ref _refCount);
        }

        public int ReleaseRef()
        {
            return Interlocked.Decrement(ref _refCount);
        }

        public void Dispose()
        {
            Gate.Dispose();
        }
    }
}
