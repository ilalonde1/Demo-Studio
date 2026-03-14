using System.Text.RegularExpressions;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.Tests;

public sealed class CompositionAndDiagnosticsTests
{
    [Fact]
    public async Task ComposeAsync_ReturnsFailure_WhenRawVideoMissing()
    {
        var launcher = new FakeProcessLauncher();
        var service = new DesktopVideoComposeService(launcher);
        var manifest = new DesktopComposeManifest(
            Guid.NewGuid(),
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4"),
            DateTimeOffset.UtcNow,
            new[]
            {
                new DesktopComposeClip(1, 1, "Clip 1", null, 0, 1.5, "00:01")
            });

        var result = await service.ComposeAsync(manifest, "ffmpeg");

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComposeAsync_WritesFinalLatest_WhenFakeLauncherSucceeds()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var raw = Path.Combine(root, "session.mp4");
        await File.WriteAllTextAsync(raw, "raw");

        try
        {
            var launcher = new FakeProcessLauncher();
            var service = new DesktopVideoComposeService(launcher);
            var manifest = new DesktopComposeManifest(
                Guid.NewGuid(),
                raw,
                DateTimeOffset.UtcNow,
                new[]
                {
                    new DesktopComposeClip(1, 1, "Clip 1", "Banner", 0.0, 1.0, "00:01"),
                    new DesktopComposeClip(2, 2, "Clip 2", null, 1.0, 1.2, "00:01")
                });

            var result = await service.ComposeAsync(manifest, "ffmpeg");

            Assert.True(result.Succeeded, result.Message);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.EndsWith("final-latest.mp4", result.OutputPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ComposeAsync_ReusesCachedFinal_OnRepeatedRun()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var raw = Path.Combine(root, "session.mp4");
        await File.WriteAllTextAsync(raw, "raw");

        try
        {
            var launcher = new FakeProcessLauncher();
            var service = new DesktopVideoComposeService(launcher);
            var manifest = new DesktopComposeManifest(
                Guid.NewGuid(),
                raw,
                DateTimeOffset.UtcNow,
                new[]
                {
                    new DesktopComposeClip(1, 1, "Clip 1", "Banner", 0.0, 1.0, "00:01"),
                    new DesktopComposeClip(2, 2, "Clip 2", null, 1.0, 1.2, "00:01")
                });

            var first = await service.ComposeAsync(manifest, "ffmpeg");
            Assert.True(first.Succeeded, first.Message);
            var firstLaunches = launcher.LaunchCount;
            Assert.True(firstLaunches > 0);

            var second = await service.ComposeAsync(manifest, "ffmpeg");
            Assert.True(second.Succeeded, second.Message);
            Assert.Equal(firstLaunches, launcher.LaunchCount);
            Assert.Contains("cache", second.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ComposeAsync_WritesComposeHealthSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var raw = Path.Combine(root, "session.mp4");
        await File.WriteAllTextAsync(raw, "raw");

        try
        {
            var launcher = new FakeProcessLauncher();
            var service = new DesktopVideoComposeService(launcher);
            var manifest = new DesktopComposeManifest(
                Guid.NewGuid(),
                raw,
                DateTimeOffset.UtcNow,
                new[]
                {
                    new DesktopComposeClip(1, 1, "Clip 1", "Banner", 0.0, 1.0, "00:01")
                });

            var result = await service.ComposeAsync(manifest, "ffmpeg");
            Assert.True(result.Succeeded, result.Message);

            var curatedDirectory = Path.Combine(root, "curated");
            var healthPath = Path.Combine(curatedDirectory, "compose-health.json");
            Assert.True(File.Exists(healthPath));

            var rawJson = await File.ReadAllTextAsync(healthPath);
            Assert.Contains("\"latestRun\"", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"summary\"", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"cache\"", rawJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void DiagnosticsBundle_WritesBundle_WithFailureCode()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var raw = Path.Combine(root, "session.mp4");
        File.WriteAllText(raw, "raw");
        var service = new DesktopDiagnosticsBundleService(root);

        try
        {
            var path = service.TryWriteFailureBundle(
                Guid.NewGuid(),
                raw,
                "DS-DESK-TEST-001",
                "Synthetic failure",
                null,
                new CaptureTargetSettings("Window", "MyApp", "myapp", null, true),
                "ffmpeg",
                "C:\\Program Files\\App\\app.exe",
                null,
                "C:\\Program Files\\App");

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.True(File.Exists(path));
            var rawJson = File.ReadAllText(path!);
            Assert.Contains("DS-DESK-TEST-001", rawJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task PublishPackage_CreatesZip_WithMetadataAndVideo()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var video = Path.Combine(root, "final.mp4");
        await File.WriteAllTextAsync(video, "video");

        try
        {
            var service = new DesktopPublishPackageService(new FakeProcessLauncher());
            var request = new DesktopPublishPackageRequest(
                SessionId: Guid.NewGuid(),
                SourceVideoPath: video,
                OutputRoot: Path.Combine(root, "publish"),
                Title: "Demo Title",
                Description: "Demo Description",
                QualityPreset: "Balanced",
                ExportStyle: "Portfolio Clean",
                ClipCount: 3,
                DurationSeconds: 42,
                StartedUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
                CompletedUtc: DateTimeOffset.UtcNow);

            var result = await service.CreateAsync(request, "ffmpeg");

            Assert.True(result.Succeeded, result.Message);
            Assert.NotNull(result.PackagePath);
            Assert.True(File.Exists(result.PackagePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        private static readonly Regex LastQuoted = new("\"([^\"]+)\"\\s*$", RegexOptions.Compiled);
        public int LaunchCount { get; private set; }

        public Task<IProcessHandle> StartProcessAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchCount++;
            var output = ResolveOutputPath(request.Arguments, request.ArgumentList);
            if (!string.IsNullOrWhiteSpace(output))
            {
                var dir = Path.GetDirectoryName(output);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(output, "ok");
            }

            IProcessHandle handle = new FakeProcessHandle();
            return Task.FromResult(handle);
        }

        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default)
        {
            LaunchCount++;
            var output = ResolveOutputPath(request.Arguments, request.ArgumentList);
            if (!string.IsNullOrWhiteSpace(output))
            {
                var dir = Path.GetDirectoryName(output);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(output, "ok");
            }

            return Task.FromResult(new ProcessLaunchResult(
                Started: true,
                ProcessId: 123,
                Execution: new ProcessExecutionResult(0, string.Empty, string.Empty, TimedOut: false, Cancelled: false),
                ErrorMessage: null));
        }

        private static string? ResolveOutputPath(string arguments, IReadOnlyList<string>? argumentList)
        {
            if (argumentList is { Count: > 0 })
            {
                return argumentList[^1];
            }

            var match = LastQuoted.Match(arguments ?? string.Empty);
            return match.Success ? match.Groups[1].Value : null;
        }

        private sealed class FakeProcessHandle : IProcessHandle
        {
            public int? ProcessId => 123;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<ProcessExecutionResult> WaitAsync(CancellationToken cancellationToken = default)
            {
                var result = new ProcessExecutionResult(0, string.Empty, string.Empty, TimedOut: false, Cancelled: false);
                return Task.FromResult(result);
            }
        }
    }
}
