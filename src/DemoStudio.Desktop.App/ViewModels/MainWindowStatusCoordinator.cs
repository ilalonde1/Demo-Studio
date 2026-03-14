using DemoStudio.Desktop.App.Services;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.ViewModels;

internal sealed class MainWindowStatusCoordinator
{
    private readonly ILogger _logger;
    private readonly Func<string?, string> _buildFixHint;
    private long _lastBackgroundFailureTicks;

    public MainWindowStatusCoordinator(ILogger logger, Func<string?, string> buildFixHint)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _buildFixHint = buildFixHint ?? throw new ArgumentNullException(nameof(buildFixHint));
    }

    public string BuildFailureDisplay(string code, string summary, string? detail = null)
    {
        var envelope = new DesktopFailureEnvelope(
            Code: code,
            Summary: summary,
            Detail: detail,
            FixHint: _buildFixHint(code));
        return envelope.ToDisplayText();
    }

    public string CreateRuntimeFailureMessage(string code, string summary, Exception? exception = null)
    {
        if (exception is not null)
        {
            _logger.LogError(exception, "Runtime failure {FailureCode}: {Summary}", code, summary);
        }
        else
        {
            _logger.LogWarning("Runtime failure {FailureCode}: {Summary}", code, summary);
        }

        return BuildFailureDisplay(code, summary, exception?.Message);
    }

    public async Task ReportBackgroundFailureAsync(
        string code,
        string summary,
        Exception exception,
        TimeSpan minInterval,
        Func<string, Task> applyMessageAsync)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(applyMessageAsync);

        if (!ShouldReportBackgroundFailure(minInterval))
        {
            return;
        }

        _logger.LogError(exception, "Background failure {FailureCode}: {Summary}", code, summary);
        await applyMessageAsync(CreateRuntimeFailureMessage(code, summary, exception));
    }

    private bool ShouldReportBackgroundFailure(TimeSpan minInterval)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var previousTicks = Interlocked.Read(ref _lastBackgroundFailureTicks);
        if (previousTicks != 0)
        {
            var elapsedTicks = nowTicks - previousTicks;
            if (elapsedTicks > 0 && elapsedTicks < minInterval.Ticks)
            {
                return false;
            }
        }

        Interlocked.Exchange(ref _lastBackgroundFailureTicks, nowTicks);
        return true;
    }
}
