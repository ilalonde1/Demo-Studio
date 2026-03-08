using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.Tests;

public sealed class CaptureRuntimeResilienceTests
{
    [Fact]
    public void Constructor_FallsBackToTempStorage_WhenConfiguredStorageRootIsInvalid()
    {
        var options = new DesktopRecorderOptions
        {
            StorageRoot = "bad\0path"
        };

        var runtime = new DesktopCaptureRuntime(options);

        Assert.False(string.IsNullOrWhiteSpace(runtime.StorageRoot));
        Assert.True(Directory.Exists(runtime.StorageRoot));
        Assert.Contains(Path.GetTempPath(), runtime.StorageRoot, StringComparison.OrdinalIgnoreCase);
    }
}
