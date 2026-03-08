using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Desktop.Core.Time;

namespace DemoStudio.Desktop.App.Tests.ReleaseGate;

public sealed class ReleaseConfidenceGateTests
{
    [Fact]
    [Trait("Gate", "ReleaseConfidence")]
    public async Task GoldenPath_ComposePublishAndRecovery_RoundTrips()
    {
        var root = CreateTempRoot();
        var rawPath = Path.Combine(root, "session.mp4");
        await File.WriteAllTextAsync(rawPath, "raw");

        try
        {
            var launcher = new FakeProcessLauncher();
            var composeService = new DesktopVideoComposeService(launcher);
            var composeManifest = new DesktopComposeManifest(
                Guid.NewGuid(),
                rawPath,
                DateTimeOffset.UtcNow,
                new[]
                {
                    new DesktopComposeClip(1, 1, "Intro", "Welcome", 0d, 1.0d, "00:01"),
                    new DesktopComposeClip(2, 2, "Flow", null, 1.0d, 1.1d, "00:01")
                });

            var compose = await composeService.ComposeAsync(composeManifest, "ffmpeg");
            Assert.True(compose.Succeeded, compose.Message);
            Assert.False(string.IsNullOrWhiteSpace(compose.OutputPath));
            Assert.True(File.Exists(compose.OutputPath!));

            var publishService = new DesktopPublishPackageService(launcher);
            var publish = await publishService.CreateAsync(
                new DesktopPublishPackageRequest(
                    SessionId: Guid.NewGuid(),
                    SourceVideoPath: compose.OutputPath!,
                    OutputRoot: Path.Combine(root, "publish"),
                    Title: "Release Gate Demo",
                    Description: "Golden path package",
                    QualityPreset: "Balanced",
                    ExportStyle: "Portfolio Clean",
                    ClipCount: 2,
                    DurationSeconds: 2.1d,
                    StartedUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
                    CompletedUtc: DateTimeOffset.UtcNow),
                "ffmpeg");

            Assert.True(publish.Succeeded, publish.Message);
            Assert.False(string.IsNullOrWhiteSpace(publish.PackagePath));
            Assert.True(File.Exists(publish.PackagePath!));

            var recovery = new DesktopSessionRecoveryService(root);
            var draft = new DesktopSessionDraft(
                SessionId: Guid.NewGuid(),
                State: RecorderSessionState.Paused,
                SavedUtc: DateTimeOffset.UtcNow,
                CaptureMode: "Window",
                WindowTitleContains: "Demo",
                CaptureNarration: true,
                MicrophoneDeviceName: "Default",
                QualityPreset: "Balanced",
                ExportStyle: "Portfolio Clean",
                AiProvider: "OpenAI",
                AiBaseUrl: null,
                AiModel: "gpt-4o-mini-tts",
                AiVoice: "alloy",
                AiAutoTrimScript: true,
                AiWordsPerSecond: 2.6d,
                LastOutputPath: compose.OutputPath,
                Clips: new[]
                {
                    new DesktopSessionDraftClip(1, 1, "Intro", "Welcome", "00:01", 0d, 1d, true)
                });

            await recovery.SaveAsync(draft);
            var loaded = await recovery.TryLoadAsync();
            Assert.NotNull(loaded);
            Assert.Equal(draft.CaptureMode, loaded!.CaptureMode);
            Assert.Single(loaded.Clips);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    [Trait("Gate", "ReleaseConfidence")]
    public async Task FailureModes_ReturnDeterministicMessages()
    {
        var compose = new DesktopVideoComposeService(new FakeProcessLauncher());
        var composeResult = await compose.ComposeAsync(
            new DesktopComposeManifest(
                Guid.NewGuid(),
                Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4"),
                DateTimeOffset.UtcNow,
                new[] { new DesktopComposeClip(1, 1, "Clip", null, 0d, 1d, "00:01") }),
            "ffmpeg");

        Assert.False(composeResult.Succeeded);
        Assert.Contains("not found", composeResult.Message, StringComparison.OrdinalIgnoreCase);

        var publish = new DesktopPublishPackageService(new FakeProcessLauncher());
        var publishResult = await publish.CreateAsync(
            new DesktopPublishPackageRequest(
                SessionId: Guid.NewGuid(),
                SourceVideoPath: Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4"),
                OutputRoot: Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N")),
                Title: "Demo",
                Description: "Demo",
                QualityPreset: "Balanced",
                ExportStyle: "Portfolio Clean",
                ClipCount: 1,
                DurationSeconds: 1d,
                StartedUtc: DateTimeOffset.UtcNow,
                CompletedUtc: DateTimeOffset.UtcNow),
            "ffmpeg");

        Assert.False(publishResult.Succeeded);
        Assert.Contains("source video not found", publishResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Gate", "ReleaseConfidence")]
    public void PerformanceBudgetSummary_EmitsPercentiles()
    {
        var perf = new DesktopPerformanceMetricsService(maxSamplesPerOperation: 64);
        for (var i = 1; i <= 20; i++)
        {
            _ = perf.Record("ComposeVideo", TimeSpan.FromMilliseconds(100 + i));
            _ = perf.Record("StartClip", TimeSpan.FromMilliseconds(40 + i));
        }

        var summary = perf.Record("ComposeVideo", TimeSpan.FromMilliseconds(175));
        Assert.Contains("ComposeVideo", summary, StringComparison.Ordinal);
        Assert.Contains("StartClip", summary, StringComparison.Ordinal);
        Assert.Contains("p50", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p95", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Gate", "ReleaseConfidence")]
    public async Task ArtifactContracts_ManifestAndPackageContainExpectedFiles()
    {
        var root = CreateTempRoot();
        var rawPath = Path.Combine(root, "session.mp4");
        await File.WriteAllTextAsync(rawPath, "raw");

        try
        {
            var manifestService = new DesktopComposeManifestService();
            var manifestResult = manifestService.WriteManifest(
                new DesktopComposeManifest(
                    Guid.NewGuid(),
                    rawPath,
                    DateTimeOffset.UtcNow,
                    new[] { new DesktopComposeClip(1, 1, "Clip", "Banner", 0d, 1d, "00:01") }));

            Assert.True(manifestResult.Succeeded, manifestResult.Message);
            Assert.True(File.Exists(manifestResult.OutputPath));
            var manifestJson = await File.ReadAllTextAsync(manifestResult.OutputPath!);
            Assert.Contains("\"sessionId\"", manifestJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"rawVideoPath\"", manifestJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"clips\"", manifestJson, StringComparison.OrdinalIgnoreCase);

            var package = new DesktopPublishPackageService(new FakeProcessLauncher());
            var packageResult = await package.CreateAsync(
                new DesktopPublishPackageRequest(
                    SessionId: Guid.NewGuid(),
                    SourceVideoPath: rawPath,
                    OutputRoot: Path.Combine(root, "publish"),
                    Title: "Demo",
                    Description: "Contract",
                    QualityPreset: "Balanced",
                    ExportStyle: "Portfolio Clean",
                    ClipCount: 1,
                    DurationSeconds: 1d,
                    StartedUtc: DateTimeOffset.UtcNow,
                    CompletedUtc: DateTimeOffset.UtcNow),
                "ffmpeg");

            Assert.True(packageResult.Succeeded, packageResult.Message);
            using var archive = ZipFile.OpenRead(packageResult.PackagePath!);
            Assert.Contains(archive.Entries, x => string.Equals(x.Name, "demo.mp4", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(archive.Entries, x => string.Equals(x.Name, "metadata.json", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(archive.Entries, x => string.Equals(x.Name, "share-copy.txt", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(archive.Entries, x => string.Equals(x.Name, "README.txt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    [Trait("Gate", "ReleaseConfidence")]
    public async Task CountdownCancellation_ReturnsFalseQuickly()
    {
        var vm = new MainWindowViewModel(new RecorderSessionEngine(new SystemClock()));
        try
        {
            var method = typeof(MainWindowViewModel)
                .GetMethod("RunStartCountdownAsync", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var sw = Stopwatch.StartNew();
            var task = (Task<bool>)method!.Invoke(vm, new object[] { 3, cts.Token })!;
            var result = await task;
            sw.Stop();

            Assert.False(result);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-release-gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        private static readonly Regex LastQuoted = new("\"([^\"]+)\"\\s*$", RegexOptions.Compiled);

        public Task<IProcessHandle> StartProcessAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = ResolveOutputPath(request.Arguments);
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
            cancellationToken.ThrowIfCancellationRequested();
            var output = ResolveOutputPath(request.Arguments);
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

        private static string? ResolveOutputPath(string arguments)
        {
            var match = LastQuoted.Match(arguments ?? string.Empty);
            return match.Success ? match.Groups[1].Value : null;
        }

        private sealed class FakeProcessHandle : IProcessHandle
        {
            public int? ProcessId => 123;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<ProcessExecutionResult> WaitAsync(CancellationToken cancellationToken = default)
            {
                var result = new ProcessExecutionResult(0, string.Empty, string.Empty, TimedOut: false, Cancelled: false);
                return Task.FromResult(result);
            }
        }
    }
}
