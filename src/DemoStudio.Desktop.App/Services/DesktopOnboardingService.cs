using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopOnboardingService
{
    private readonly string _markerPath;

    public DesktopOnboardingService(string storageRoot)
    {
        Directory.CreateDirectory(storageRoot);
        _markerPath = Path.Combine(storageRoot, ".onboarding-complete");
    }

    public bool ShouldShow()
    {
        return !File.Exists(_markerPath);
    }

    public void MarkComplete()
    {
        File.WriteAllText(_markerPath, DateTimeOffset.UtcNow.ToString("O"));
    }
}
