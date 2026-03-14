using DemoStudio.Application.Abstractions.System;
using DemoStudio.Capture.Abstractions.Interfaces;
using DemoStudio.Infrastructure.Execution;
using DemoStudio.Infrastructure.Execution.Windows;
using DemoStudio.Infrastructure.Options;
using DemoStudio.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopVideoCaptureServiceFactory
{
    private readonly IProcessLauncher _processLauncher;
    private readonly ILogger<FfmpegVideoCaptureService> _logger;

    public DesktopVideoCaptureServiceFactory(
        IProcessLauncher processLauncher,
        ILogger<FfmpegVideoCaptureService> logger)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IVideoCaptureService Create(FfmpegCaptureOptions options, string storageRoot, IWindowLocator windowLocator)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(windowLocator);

        var fileStorage = new LocalFileStorage(storageRoot);
        return new FfmpegVideoCaptureService(
            _processLauncher,
            fileStorage,
            windowLocator,
            Options.Create(options),
            _logger);
    }
}
