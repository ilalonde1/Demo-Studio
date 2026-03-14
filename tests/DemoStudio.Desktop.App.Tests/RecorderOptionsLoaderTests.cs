using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.Tests;

public sealed class RecorderOptionsLoaderTests
{
    [Fact]
    public void Load_BindsConfiguredValuesThroughConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var json = """
            {
              "DesktopRecorder": {
                  "StorageRoot": "   ",
                  "Capture": {
                  "FfmpegPath": "   ffmpeg.exe   ",
                  "FrameRate": 24,
                  "Crf": 20,
                  "MaxDurationSeconds": 1200,
                  "OutputFileExtension": ".mov",
                  "CaptureMode": "Desktop",
                  "Preset": "FAST",
                  "WindowTitleRegex": "[unterminated"
                }
              }
            }
            """;
            File.WriteAllText(Path.Combine(root, "appsettings.json"), json);

            var options = DesktopRecorderOptionsLoader.Load(root);

            Assert.False(string.IsNullOrWhiteSpace(options.StorageRoot));
            Assert.Equal("ffmpeg.exe", options.Capture.FfmpegPath);
            Assert.Equal(24, options.Capture.FrameRate);
            Assert.Equal(20, options.Capture.Crf);
            Assert.Equal(1200, options.Capture.MaxDurationSeconds);
            Assert.Equal(".mov", options.Capture.OutputFileExtension);
            Assert.Equal("Desktop", options.Capture.CaptureMode);
            Assert.Equal("fast", options.Capture.Preset);
            Assert.Null(options.Capture.WindowTitleRegex);
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
    public void Load_AcceptsValidConfiguredValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var json = """
            {
              "DesktopRecorder": {
                "StorageRoot": "C:\\\\DemoStudio\\\\Recorder",
                "Capture": {
                  "FfmpegPath": "C:\\\\Tools\\\\ffmpeg.exe",
                  "FrameRate": 60,
                  "Crf": 18,
                  "MaxDurationSeconds": 3600,
                  "OutputFileExtension": ".mkv",
                  "CaptureMode": "Desktop",
                  "Preset": "fast",
                  "WindowTitleRegex": "Demo.*"
                }
              }
            }
            """;
            File.WriteAllText(Path.Combine(root, "appsettings.json"), json);

            var options = DesktopRecorderOptionsLoader.Load(root);

            Assert.Equal(@"C:\\DemoStudio\\Recorder", options.StorageRoot);
            Assert.Equal(@"C:\\Tools\\ffmpeg.exe", options.Capture.FfmpegPath);
            Assert.Equal(60, options.Capture.FrameRate);
            Assert.Equal(18, options.Capture.Crf);
            Assert.Equal(3600, options.Capture.MaxDurationSeconds);
            Assert.Equal(".mkv", options.Capture.OutputFileExtension);
            Assert.Equal("Desktop", options.Capture.CaptureMode);
            Assert.Equal("fast", options.Capture.Preset);
            Assert.Equal("Demo.*", options.Capture.WindowTitleRegex);
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
