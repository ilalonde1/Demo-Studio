using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.Tests.Helpers;
using DemoStudio.Infrastructure.Storage;

namespace DemoStudio.Desktop.App.Tests;

public sealed class Phase1StabilityTests
{
    [Fact]
    public async Task LocalFileStorage_SaveAsync_RejectsTraversalOutsideSandbox()
    {
        var root = CreateTempRoot();

        try
        {
            var storage = new LocalFileStorage(root);
            await using var stream = new MemoryStream("data"u8.ToArray());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(@"..\escape.txt", stream));

            Assert.Contains("outside the allowed storage root", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task LocalFileStorage_ReadAllTextAsync_RejectsRootedPathOutsideSandbox()
    {
        var root = CreateTempRoot();
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "outside");

        try
        {
            var storage = new LocalFileStorage(root);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.ReadAllTextAsync(outside));

            Assert.Contains("outside the allowed storage root", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(outside))
            {
                File.Delete(outside);
            }

            SafeDelete(root);
        }
    }

    [Fact]
    public async Task LocalFileStorage_SaveAsync_AllowsRootedPathInsideSandbox()
    {
        var root = CreateTempRoot();
        var inside = Path.Combine(root, "nested", "data.txt");

        try
        {
            var storage = new LocalFileStorage(root);
            await using var stream = new MemoryStream("ok"u8.ToArray());

            var saved = await storage.SaveAsync(inside, stream);

            Assert.Equal(Path.GetFullPath(inside), saved);
            Assert.True(File.Exists(saved));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task SessionHistoryService_UsesBackupWhenPrimaryIsCorrupt()
    {
        var root = CreateTempRoot();

        try
        {
            var service = new DesktopSessionHistoryService(root);
            var first = new DesktopSessionRecord(Guid.NewGuid(), "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, "a", 1, null, 0, null);
            var second = new DesktopSessionRecord(Guid.NewGuid(), "Completed", DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow.AddMinutes(1), 2, "b", 2, null, 0, null);

            await service.UpsertAsync(first);
            await service.UpsertAsync(second);

            await File.WriteAllTextAsync(Path.Combine(root, "session-history.json"), "{ not json");

            var loaded = await service.ListAsync();

            Assert.Single(loaded);
            Assert.Equal(first.SessionId, loaded[0].SessionId);
            Assert.Contains("backup", service.LastLoadDiagnostic ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task LaunchProfileService_ThrowsWhenStateIsCorruptAndNoBackupExists()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "launch-profiles.json");
        await File.WriteAllTextAsync(path, "{ invalid");

        try
        {
            var service = new DesktopLaunchProfileService(root);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync());

            Assert.Contains("corrupt", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(service.LastLoadDiagnostic));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task SessionRecoveryService_UsesAtomicSaveAndReportsBackupRecovery()
    {
        var root = CreateTempRoot();

        try
        {
            var service = new DesktopSessionRecoveryService(root);
            var first = BuildDraft("Window");
            var second = BuildDraft("Desktop");

            await service.SaveAsync(first);
            await service.SaveAsync(second);

            await File.WriteAllTextAsync(Path.Combine(root, "session-draft.json"), "[]");

            var loaded = await service.TryLoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal(first.CaptureMode, loaded!.CaptureMode);
            Assert.Contains("backup", service.LastLoadDiagnostic ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task MainWindowViewModel_Dispose_PersistsDraftDuringShutdown()
    {
        var root = CreateTempRoot();
        var vm = MainWindowViewModelTestBuilder.CreateMinimal(root);

        try
        {
            vm.Dispose();

            var draftPath = Path.Combine(root, "session-draft.json");
            Assert.True(File.Exists(draftPath));

            var draft = await File.ReadAllTextAsync(draftPath);
            Assert.Contains("\"sessionId\"", draft, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await vm.DisposeAsync();
            SafeDelete(root);
        }
    }

    private static DesktopSessionDraft BuildDraft(string captureMode)
    {
        return new DesktopSessionDraft(
            Guid.NewGuid(),
            DemoStudio.Desktop.Core.Sessions.RecorderSessionState.Paused,
            DateTimeOffset.UtcNow,
            captureMode,
            "Demo",
            true,
            "Default",
            "Balanced",
            "Portfolio Clean",
            "OpenAI",
            null,
            "gpt-4o-mini-tts",
            "alloy",
            true,
            2.6d,
            null,
            new[]
            {
                new DesktopSessionDraftClip(1, 1, "Clip 1", null, "00:01", 0d, 1d, true)
            });
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-phase1-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void SafeDelete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
