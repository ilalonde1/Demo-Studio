using DemoStudio.Application.Services;

namespace DemoStudio.Desktop.App.Tests;

public sealed class RuntimePathResolverDeterminismTests
{
    [Fact]
    public void TryResolveDesktopExecutable_PrefersKnownBuildLayoutOverArbitraryNestedExecutable()
    {
        var root = CreateTempRoot();
        var applicationDirectory = Path.Combine(root, "DemoTarget");
        var preferredExecutable = Path.Combine(applicationDirectory, "bin", "Debug", "net8.0-windows", "DemoTarget.exe");
        var arbitraryExecutable = Path.Combine(applicationDirectory, "tools", "rogue", "trap.exe");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(preferredExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(arbitraryExecutable)!);
            File.WriteAllText(preferredExecutable, "preferred");
            File.WriteAllText(arbitraryExecutable, "rogue");

            var resolved = RuntimePathResolver.TryResolveDesktopExecutable(applicationDirectory, out var fullPath, root);

            Assert.True(resolved);
            Assert.Equal(Path.GetFullPath(preferredExecutable), fullPath);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public void TryResolveDesktopExecutable_DoesNotScanArbitraryNestedExecutablesOutsideKnownLayouts()
    {
        var root = CreateTempRoot();
        var applicationDirectory = Path.Combine(root, "DemoTarget");
        var arbitraryExecutable = Path.Combine(applicationDirectory, "tools", "rogue", "trap.exe");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(arbitraryExecutable)!);
            File.WriteAllText(arbitraryExecutable, "rogue");

            var resolved = RuntimePathResolver.TryResolveDesktopExecutable(applicationDirectory, out var fullPath, root);

            Assert.False(resolved);
            Assert.Equal(string.Empty, fullPath);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-runtimepath-tests", Guid.NewGuid().ToString("N"));
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
