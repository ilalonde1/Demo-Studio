namespace DemoStudio.Infrastructure.Execution;

using System.Collections.Concurrent;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Capture.Abstractions.Interfaces;
using DemoStudio.Infrastructure.Execution.Internal;
using DemoStudio.Infrastructure.Execution.Windows;
using DemoStudio.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class FfmpegVideoCaptureService : IVideoCaptureService
{
    private static readonly TimeSpan MinimumCaptureDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CaptureProcessWaitTimeout = TimeSpan.FromSeconds(20);
    private readonly IProcessLauncher _processLauncher;
    private readonly IFileStorage _fileStorage;
    private readonly IWindowLocator _windowLocator;
    private readonly FfmpegCaptureOptions _options;
    private readonly ILogger<FfmpegVideoCaptureService> _logger;
    private readonly ConcurrentDictionary<Guid, CaptureProcessState> _activeCaptureProcesses = new();

    public FfmpegVideoCaptureService(
        IProcessLauncher processLauncher,
        IFileStorage fileStorage,
        IWindowLocator windowLocator,
        IOptions<FfmpegCaptureOptions> options,
        ILogger<FfmpegVideoCaptureService> logger)
    {
        _processLauncher = processLauncher;
        _fileStorage = fileStorage;
        _windowLocator = windowLocator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CaptureSessionStartResult> StartAsync(CaptureStartRequest request, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new CaptureSessionStartResult(false, null, "FFmpeg capture provider is disabled by configuration.");
        }

        if (!_activeCaptureProcesses.TryAdd(request.Run.Id, CaptureProcessState.Pending))
        {
            return new CaptureSessionStartResult(false, null, $"Capture is already active for run '{request.Run.Id}'.");
        }

        try
        {
            var rawVideoPath = ResolveRawVideoPath(request);
            var (arguments, modeUsed, windowTitle, processId, bounds) = await BuildStartArgumentsAsync(request, rawVideoPath, cancellationToken);

            _logger.LogInformation("Starting FFmpeg capture for run {RunId} in mode {Mode} with executable {Executable}.", request.Run.Id, modeUsed, _options.FfmpegPath);

            if (!string.IsNullOrWhiteSpace(windowTitle) && bounds.HasValue)
            {
                _logger.LogInformation(
                    "Window capture target selected for run {RunId}: title={Title}, processId={ProcessId}, rect={X},{Y},{W}x{H}.",
                    request.Run.Id,
                    windowTitle,
                    processId,
                    bounds.Value.X,
                    bounds.Value.Y,
                    bounds.Value.Width,
                    bounds.Value.Height);
            }

            var handle = await _processLauncher.StartProcessAsync(
                new ProcessStartRequest(_options.FfmpegPath, arguments, request.OutputDirectory, CaptureProcessWaitTimeout),
                cancellationToken);

            var state = new CaptureProcessState(handle, rawVideoPath, modeUsed, windowTitle, processId, bounds);
            _activeCaptureProcesses[request.Run.Id] = state;

            return new CaptureSessionStartResult(true, rawVideoPath, null);
        }
        catch (Exception ex)
        {
            _activeCaptureProcesses.TryRemove(request.Run.Id, out _);
            _logger.LogError(ex, "Failed to start FFmpeg capture for run {RunId}.", request.Run.Id);
            return new CaptureSessionStartResult(false, null, ex.Message);
        }
    }

    public async Task<CaptureSessionStopResult> StopAsync(CaptureStopRequest request, CancellationToken cancellationToken = default)
    {
        if (!_activeCaptureProcesses.TryRemove(request.Run.Id, out var state))
        {
            var exists = await _fileStorage.ExistsAsync(request.RawVideoPath, cancellationToken);
            return exists
                ? new CaptureSessionStopResult(true, null)
                : new CaptureSessionStopResult(false, $"No active FFmpeg capture process found for run '{request.Run.Id}'.");
        }

        if (state.Handle is null)
        {
            return new CaptureSessionStopResult(false, $"No FFmpeg process handle is available for run '{request.Run.Id}'.");
        }

        try
        {
            var elapsed = DateTimeOffset.UtcNow - state.StartedAtUtc;
            if (elapsed < MinimumCaptureDuration)
            {
                var delay = MinimumCaptureDuration - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            await state.Handle.StopAsync(cancellationToken);

            var execution = await state.Handle.WaitAsync(cancellationToken);
            var outputExists = await _fileStorage.ExistsAsync(state.RawVideoPath ?? request.RawVideoPath, cancellationToken);

            if (!outputExists)
            {
                return new CaptureSessionStopResult(false, "FFmpeg stopped but output video file was not found.");
            }

            if (execution.TimedOut)
            {
                return new CaptureSessionStopResult(false, "FFmpeg capture timed out.");
            }

            if (execution.Cancelled)
            {
                return new CaptureSessionStopResult(false, "FFmpeg capture was cancelled.");
            }

            return new CaptureSessionStopResult(true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("FFmpeg capture stop was cancelled for run {RunId}.", request.Run.Id);
            return new CaptureSessionStopResult(false, "FFmpeg capture stop was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed stopping FFmpeg capture for run {RunId}.", request.Run.Id);
            return new CaptureSessionStopResult(false, ex.Message);
        }
        finally
        {
            await state.Handle.DisposeAsync();
        }
    }

    private async Task<(string Arguments, string ModeUsed, string? WindowTitle, int? ProcessId, WindowBounds? Bounds)> BuildStartArgumentsAsync(
        CaptureStartRequest request,
        string rawVideoPath,
        CancellationToken cancellationToken)
    {
        if (!_options.CaptureMode.Equals("Window", StringComparison.OrdinalIgnoreCase))
        {
            return (FfmpegCommandBuilder.BuildFullDesktopCaptureArguments(_options, rawVideoPath), "Desktop", null, null, null);
        }

        var locateResult = await _windowLocator.FindAsync(
            new WindowLocatorRequest(
                _options.WindowTitleContains,
                _options.WindowTitleRegex,
                _options.WindowProcessName,
                _options.WindowHandleHex,
                _options.PreferExactHandle),
            cancellationToken);

        if (locateResult.Found && locateResult.Bounds is { IsValid: true } bounds)
        {
            var desktopBounds = locateResult.DesktopBounds is { IsValid: true } discoveredDesktopBounds
                ? discoveredDesktopBounds
                : bounds;

            var captureBounds = _options.CropEnabled
                ? CaptureRegionCalculator.ExpandWithinDesktop(bounds, desktopBounds, _options.CropPaddingPixels)
                : bounds;

            if (!captureBounds.IsValid)
            {
                throw new InvalidOperationException("Resolved window capture bounds are invalid.");
            }

            // gdigrab offsets are relative to desktop capture origin; normalize to desktop bounds.
            var normalizedBounds = new WindowBounds(
                captureBounds.X - desktopBounds.X,
                captureBounds.Y - desktopBounds.Y,
                captureBounds.Width,
                captureBounds.Height);
            if (!normalizedBounds.IsValid)
            {
                throw new InvalidOperationException("Normalized window capture bounds are invalid.");
            }

            return (
                FfmpegCommandBuilder.BuildWindowCaptureArguments(_options, normalizedBounds, rawVideoPath),
                "Window",
                locateResult.Title,
                locateResult.ProcessId,
                captureBounds);
        }

        throw new InvalidOperationException(locateResult.FailureReason ?? "Window capture target not found.");
    }

    private string ResolveRawVideoPath(CaptureStartRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RawVideoPath))
        {
            return request.RawVideoPath;
        }

        var extension = _options.OutputFileExtension.StartsWith('.')
            ? _options.OutputFileExtension
            : $".{_options.OutputFileExtension}";

        return Path.Combine(request.OutputDirectory, $"raw{extension}");
    }

    private sealed class CaptureProcessState
    {
        public static CaptureProcessState Pending { get; } = new();

        private CaptureProcessState()
        {
        }

        public CaptureProcessState(
            IProcessHandle handle,
            string rawVideoPath,
            string modeUsed,
            string? windowTitle,
            int? windowProcessId,
            WindowBounds? windowBounds)
        {
            Handle = handle;
            RawVideoPath = rawVideoPath;
            ModeUsed = modeUsed;
            WindowTitle = windowTitle;
            WindowProcessId = windowProcessId;
            WindowBounds = windowBounds;
            StartedAtUtc = DateTimeOffset.UtcNow;
        }

        public IProcessHandle? Handle { get; }

        public string? RawVideoPath { get; }

        public string? ModeUsed { get; }

        public string? WindowTitle { get; }

        public int? WindowProcessId { get; }

        public WindowBounds? WindowBounds { get; }

        public DateTimeOffset StartedAtUtc { get; }
    }
}
