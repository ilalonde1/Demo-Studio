using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.Tests;

public sealed class DesktopRuntimeLogServiceTests
{
    [Fact]
    public void WritesAndRotatesLogFiles_WhenSizeThresholdReached()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new DesktopRuntimeLogService(
                storageRoot: root,
                maxFileBytes: 240,
                maxFiles: 3,
                maxAge: TimeSpan.FromDays(14));

            for (var i = 0; i < 40; i++)
            {
                service.Info($"rotation-test-{i}-{new string('x', 40)}", "RotationTest");
            }

            service.PruneNow();
            var logsRoot = Path.Combine(root, "logs");
            var files = Directory.GetFiles(logsRoot, "runtime-*.log", SearchOption.TopDirectoryOnly);
            Assert.NotEmpty(files);
            Assert.True(files.Length <= 3, $"Expected <= 3 log files but found {files.Length}.");
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
    public void PruneNow_RemovesExpiredLogs_ByAge()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var logsRoot = Path.Combine(root, "logs");
            Directory.CreateDirectory(logsRoot);
            var oldPath = Path.Combine(logsRoot, "runtime-20240101.log");
            var newPath = Path.Combine(logsRoot, "runtime-20260101.log");
            File.WriteAllText(oldPath, "old");
            File.WriteAllText(newPath, "new");
            File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(newPath, DateTime.UtcNow);

            var service = new DesktopRuntimeLogService(
                storageRoot: root,
                maxFileBytes: 1024 * 1024,
                maxFiles: 20,
                maxAge: TimeSpan.FromDays(7));

            service.PruneNow();

            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(newPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
