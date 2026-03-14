using DemoStudio.Capture.Abstractions.Interfaces;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Domain.Entities;
using DemoStudio.Infrastructure.Execution;
using DemoStudio.Infrastructure.Execution.Windows;
using DemoStudio.Infrastructure.Options;
using DemoStudio.Infrastructure.Process;
using System.IO;
using Microsoft.Extensions.Options;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopCaptureRuntime
{
    private const int StartupAttempts = 3;
    private static readonly TimeSpan StartupProbeTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan StartupProbeInterval = TimeSpan.FromMilliseconds(250);
    private readonly object _sync = new();
    private readonly DesktopVideoCaptureServiceFactory _captureServiceFactory;
    private readonly IWindowLocator _windowLocator;
    private readonly FfmpegCaptureOptions _baseCaptureOptions;
    private readonly string _storageRoot;
    private readonly string _ffmpegPath;
    private readonly bool _isFfmpegAvailable;

    private DemoRun? _activeRun;
    private string? _activeRawPath;
    private IVideoCaptureService? _activeCaptureService;
    private bool _started;

    public DesktopCaptureRuntime(
        IOptions<DesktopRecorderOptions> options,
        IProcessLauncher processLauncher,
        DesktopVideoCaptureServiceFactory captureServiceFactory,
        IWindowLocator windowLocator)
        : this(options.Value, processLauncher, captureServiceFactory, windowLocator)
    {
    }

    public DesktopCaptureRuntime(
        DesktopRecorderOptions options,
        IProcessLauncher processLauncher,
        DesktopVideoCaptureServiceFactory captureServiceFactory,
        IWindowLocator windowLocator)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        ArgumentNullException.ThrowIfNull(processLauncher);
        _captureServiceFactory = captureServiceFactory ?? throw new ArgumentNullException(nameof(captureServiceFactory));
        _windowLocator = windowLocator ?? throw new ArgumentNullException(nameof(windowLocator));

        _storageRoot = DesktopRecorderOptionsNormalizer.ResolveStorageRoot(options.StorageRoot);

        _baseCaptureOptions = options.Capture ?? new FfmpegCaptureOptions();
        _baseCaptureOptions.Enabled = true;
        _isFfmpegAvailable = FfmpegExecutableResolver.TryResolve(_baseCaptureOptions.FfmpegPath, out var resolvedFfmpegPath);
        _ffmpegPath = _isFfmpegAvailable ? resolvedFfmpegPath : (_baseCaptureOptions.FfmpegPath ?? string.Empty);
    }

    public string? LastRawVideoPath { get; private set; }

    public string StorageRoot => _storageRoot;

    public string FfmpegPath => _ffmpegPath;
    public bool IsFfmpegAvailable => _isFfmpegAvailable;
    public bool CaptureMicrophoneEnabled => _baseCaptureOptions.CaptureMicrophone;
    public string? MicrophoneDeviceName => _baseCaptureOptions.MicrophoneDeviceName;

    public void SetMicrophoneCapture(bool enabled, string? deviceName)
    {
        lock (_sync)
        {
            _baseCaptureOptions.CaptureMicrophone = enabled;
            _baseCaptureOptions.MicrophoneDeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim();
        }
    }

    public CaptureTargetSettings GetDefaultTargetSettings()
    {
        return new CaptureTargetSettings(
            Mode: _baseCaptureOptions.CaptureMode,
            WindowTitleContains: _baseCaptureOptions.WindowTitleContains,
            WindowProcessName: _baseCaptureOptions.WindowProcessName,
            WindowHandleHex: _baseCaptureOptions.WindowHandleHex,
            FallbackToDesktop: _baseCaptureOptions.FallbackToDesktop);
    }

    public async Task<CaptureRuntimeResult> EnsureStartedAsync(CaptureTargetSettings targetSettings, CancellationToken cancellationToken = default)
    {
        DemoRun run;
        string outputDirectory;
        string rawVideoPath;
        IVideoCaptureService captureService;
        var effectiveTargetSettings = targetSettings;
        var desktopFallbackAttempted = false;

        lock (_sync)
        {
            if (_started)
            {
                return CaptureRuntimeResult.Success(_activeRawPath, null);
            }

            var sessionId = Guid.NewGuid();
            var startedAt = DateTimeOffset.UtcNow;
            outputDirectory = Path.Combine(_storageRoot, $"{startedAt:yyyyMMdd_HHmmss}_{sessionId:N}");
            Directory.CreateDirectory(outputDirectory);
            rawVideoPath = Path.Combine(outputDirectory, $"session-{sessionId:D}.mp4");

            run = new DemoRun(Guid.NewGuid(), Guid.NewGuid(), "desktop.user@demostudio.local");
            _activeRun = run;
            _activeRawPath = rawVideoPath;

            captureService = BuildCaptureService(effectiveTargetSettings);
            _activeCaptureService = captureService;
        }

        string? effectiveRawPath = null;
        CaptureRuntimeResult startupHealthy = CaptureRuntimeResult.Failure("Capture startup verification failed.");
        var startFailureMessage = "Capture start failed.";
        var attempt = 0;

        while (true)
        {
            var maxAttemptsForCurrentMode = ResolveMaxAttempts(effectiveTargetSettings);
            attempt++;
            var start = await captureService.StartAsync(new CaptureStartRequest(run, outputDirectory, rawVideoPath), cancellationToken);
            if (!start.Succeeded)
            {
                startFailureMessage = start.ErrorMessage ?? "Capture start failed.";
                if (attempt < maxAttemptsForCurrentMode)
                {
                    await Task.Delay(350, cancellationToken);
                    continue;
                }

                if (!desktopFallbackAttempted
                    && effectiveTargetSettings.FallbackToDesktop
                    && string.Equals(effectiveTargetSettings.Mode, "Window", StringComparison.OrdinalIgnoreCase))
                {
                    desktopFallbackAttempted = true;
                    attempt = 0;
                    effectiveTargetSettings = new CaptureTargetSettings(
                        Mode: "Desktop",
                        WindowTitleContains: null,
                        WindowProcessName: null,
                        WindowHandleHex: null,
                        FallbackToDesktop: false);
                    captureService = BuildCaptureService(effectiveTargetSettings);
                    lock (_sync)
                    {
                        _activeCaptureService = captureService;
                    }

                    continue;
                }

                lock (_sync)
                {
                    _activeRun = null;
                    _activeRawPath = null;
                    _activeCaptureService = null;
                }

                return CaptureRuntimeResult.Failure(startFailureMessage);
            }

            effectiveRawPath = string.IsNullOrWhiteSpace(start.RawVideoPath) ? rawVideoPath : start.RawVideoPath;
            startupHealthy = await WaitForCaptureStartupAsync(effectiveRawPath, cancellationToken);
            if (startupHealthy.Succeeded)
            {
                break;
            }

            var startupStop = await captureService.StopAsync(new CaptureStopRequest(run, effectiveRawPath), cancellationToken);
            if (!startupStop.Succeeded && !string.IsNullOrWhiteSpace(startupStop.ErrorMessage))
            {
                startupHealthy = CaptureRuntimeResult.Failure(
                    $"Capture startup handshake failed: {startupStop.ErrorMessage}");
            }

            if (attempt < maxAttemptsForCurrentMode)
            {
                await Task.Delay(350, cancellationToken);
                continue;
            }

            if (!desktopFallbackAttempted
                && effectiveTargetSettings.FallbackToDesktop
                && string.Equals(effectiveTargetSettings.Mode, "Window", StringComparison.OrdinalIgnoreCase))
            {
                desktopFallbackAttempted = true;
                attempt = 0;
                effectiveTargetSettings = new CaptureTargetSettings(
                    Mode: "Desktop",
                    WindowTitleContains: null,
                    WindowProcessName: null,
                    WindowHandleHex: null,
                    FallbackToDesktop: false);
                captureService = BuildCaptureService(effectiveTargetSettings);
                lock (_sync)
                {
                    _activeCaptureService = captureService;
                }
            }
            else
            {
                break;
            }
        }

        if (!startupHealthy.Succeeded)
        {
            lock (_sync)
            {
                _started = false;
                _activeRun = null;
                _activeRawPath = null;
                _activeCaptureService = null;
            }

            return CaptureRuntimeResult.Failure(startupHealthy.ErrorMessage ?? "Capture startup verification failed.");
        }

        lock (_sync)
        {
            _started = true;
            LastRawVideoPath = effectiveRawPath;
        }

        return CaptureRuntimeResult.Success(LastRawVideoPath, null);
    }

    private static int ResolveMaxAttempts(CaptureTargetSettings settings)
    {
        if (string.Equals(settings.Mode, "Window", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return StartupAttempts;
    }

    public async Task<CaptureRuntimeResult> StopAsync(CancellationToken cancellationToken = default)
    {
        DemoRun? run;
        string? rawPath;
        IVideoCaptureService? captureService;

        lock (_sync)
        {
            if (!_started || _activeRun is null || string.IsNullOrWhiteSpace(_activeRawPath))
            {
                return CaptureRuntimeResult.Success(LastRawVideoPath, null);
            }

            run = _activeRun;
            rawPath = _activeRawPath;
            captureService = _activeCaptureService;
        }

        if (captureService is null)
        {
            return CaptureRuntimeResult.Failure("Capture runtime state is invalid (capture service missing).");
        }

        var stop = await captureService.StopAsync(new CaptureStopRequest(run, rawPath), cancellationToken);

        lock (_sync)
        {
            _started = false;
            _activeRun = null;
            _activeRawPath = null;
            _activeCaptureService = null;
        }

        if (!stop.Succeeded)
        {
            return CaptureRuntimeResult.Failure(stop.ErrorMessage ?? "Capture stop failed.");
        }

        var finalizeHealthy = await WaitForFinalizedOutputAsync(LastRawVideoPath, cancellationToken);
        if (!finalizeHealthy.Succeeded)
        {
            return finalizeHealthy;
        }

        return CaptureRuntimeResult.Success(LastRawVideoPath, null);
    }

    private static async Task<CaptureRuntimeResult> WaitForCaptureStartupAsync(string? rawVideoPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawVideoPath))
        {
            return CaptureRuntimeResult.Failure("Capture startup failed: raw output path is empty.");
        }

        var deadline = DateTimeOffset.UtcNow + StartupProbeTimeout;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetFileLength(rawVideoPath, out var length) && length > 0)
            {
                return CaptureRuntimeResult.Success(rawVideoPath, null);
            }

            await Task.Delay(StartupProbeInterval, cancellationToken);
        }

        return CaptureRuntimeResult.Failure("Capture startup handshake failed: output file did not begin writing bytes within timeout.");
    }

    private static async Task<CaptureRuntimeResult> WaitForFinalizedOutputAsync(string? rawVideoPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawVideoPath))
        {
            return CaptureRuntimeResult.Failure("Capture finalize failed: raw output path is empty.");
        }

        var stableReads = 0;
        long previousLength = -1;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetFileLength(rawVideoPath, out var length) && length > 0)
            {
                if (length == previousLength)
                {
                    stableReads++;
                    if (stableReads >= 2)
                    {
                        return CaptureRuntimeResult.Success(rawVideoPath, null);
                    }
                }
                else
                {
                    stableReads = 0;
                    previousLength = length;
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        return CaptureRuntimeResult.Failure("Capture finalize failed: output file is unavailable or remained empty.");
    }

    private static bool TryGetFileLength(string path, out long length)
    {
        length = 0;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            length = stream.Length;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private IVideoCaptureService BuildCaptureService(CaptureTargetSettings settings)
    {
        var normalizedMode = string.Equals(settings.Mode, "Desktop", StringComparison.OrdinalIgnoreCase)
            ? "Desktop"
            : "Window";
        var hasExactHandle = !string.IsNullOrWhiteSpace(settings.WindowHandleHex);
        var effectiveOptions = new FfmpegCaptureOptions
        {
            Enabled = true,
            FfmpegPath = _ffmpegPath,
            FrameRate = _baseCaptureOptions.FrameRate,
            VideoCodec = _baseCaptureOptions.VideoCodec,
            Preset = _baseCaptureOptions.Preset,
            Crf = _baseCaptureOptions.Crf,
            MaxDurationSeconds = _baseCaptureOptions.MaxDurationSeconds,
            OutputFileExtension = _baseCaptureOptions.OutputFileExtension,
            CaptureMode = normalizedMode,
            WindowTitleContains = hasExactHandle
                ? null
                : string.IsNullOrWhiteSpace(settings.WindowTitleContains)
                    ? null
                    : settings.WindowTitleContains.Trim(),
            WindowTitleRegex = null,
            WindowProcessName = string.IsNullOrWhiteSpace(settings.WindowProcessName) ? null : settings.WindowProcessName.Trim(),
            WindowHandleHex = string.IsNullOrWhiteSpace(settings.WindowHandleHex) ? null : settings.WindowHandleHex.Trim(),
            PreferExactHandle = hasExactHandle,
            FallbackToDesktop = settings.FallbackToDesktop,
            CropEnabled = _baseCaptureOptions.CropEnabled,
            CropPaddingPixels = _baseCaptureOptions.CropPaddingPixels,
            HighlightCursor = _baseCaptureOptions.HighlightCursor,
            CaptureMicrophone = _baseCaptureOptions.CaptureMicrophone,
            MicrophoneDeviceName = string.IsNullOrWhiteSpace(_baseCaptureOptions.MicrophoneDeviceName) ? null : _baseCaptureOptions.MicrophoneDeviceName.Trim()
        };

        return _captureServiceFactory.Create(effectiveOptions, _storageRoot, _windowLocator);
    }
}

public sealed record CaptureRuntimeResult(bool Succeeded, string? RawVideoPath, string? ErrorMessage)
{
    public static CaptureRuntimeResult Success(string? rawVideoPath, string? message) => new(true, rawVideoPath, message);

    public static CaptureRuntimeResult Failure(string error) => new(false, null, error);
}

public sealed record CaptureTargetSettings(
    string Mode,
    string? WindowTitleContains,
    string? WindowProcessName,
    string? WindowHandleHex,
    bool FallbackToDesktop);
