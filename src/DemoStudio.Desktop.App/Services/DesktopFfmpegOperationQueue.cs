using System.Diagnostics;
using System.Threading;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopFfmpegOperationQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _maxConcurrentAndQueued;
    private int _inFlightAndQueued;

    public DesktopFfmpegOperationQueue(int maxConcurrentAndQueued = 2)
    {
        if (maxConcurrentAndQueued < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentAndQueued), "Queue capacity must be at least 1.");
        }

        _maxConcurrentAndQueued = maxConcurrentAndQueued;
    }

    public async Task<DesktopFfmpegQueueResult<T>> EnqueueAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        var normalizedName = string.IsNullOrWhiteSpace(operationName) ? "FFmpeg operation" : operationName.Trim();
        var slot = Interlocked.Increment(ref _inFlightAndQueued);
        if (slot > _maxConcurrentAndQueued)
        {
            Interlocked.Decrement(ref _inFlightAndQueued);
            return DesktopFfmpegQueueResult<T>.FromRejected($"{normalizedName} skipped: render queue is full.");
        }

        var queueStopwatch = Stopwatch.StartNew();
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var queueDelay = queueStopwatch.Elapsed;
                var value = await operation(cancellationToken);
                return DesktopFfmpegQueueResult<T>.FromAccepted(value, queueDelay);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightAndQueued);
        }
    }
}

public sealed record DesktopFfmpegQueueResult<T>(bool Accepted, T? Value, string Message, TimeSpan QueueDelay)
    where T : class
{
    public static DesktopFfmpegQueueResult<T> FromAccepted(T value, TimeSpan queueDelay)
        => new(true, value, string.Empty, queueDelay);

    public static DesktopFfmpegQueueResult<T> FromRejected(string message)
        => new(false, null, message, TimeSpan.Zero);
}
