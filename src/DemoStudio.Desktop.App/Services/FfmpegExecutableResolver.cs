using System.IO;

namespace DemoStudio.Desktop.App.Services;

internal static class FfmpegExecutableResolver
{
    public static bool TryResolve(string? configuredPath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return false;
        }

        var normalized = configuredPath.Trim();
        if (Path.IsPathRooted(normalized) ||
            normalized.Contains(Path.DirectorySeparatorChar) ||
            normalized.Contains(Path.AltDirectorySeparatorChar))
        {
            var full = Path.GetFullPath(normalized);
            if (!File.Exists(full))
            {
                return false;
            }

            resolvedPath = full;
            return true;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathSegments = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extEnv = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(extEnv)
            ? new[] { ".exe", ".com" }
            : extEnv
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(IsNativeExecutableExtension)
                .ToArray();

        var hasExtension = Path.HasExtension(normalized);
        foreach (var segment in pathSegments)
        {
            if (!Directory.Exists(segment))
            {
                continue;
            }

            if (hasExtension)
            {
                var candidate = Path.Combine(segment, normalized);
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }

                continue;
            }

            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(segment, normalized + ext);
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsNativeExecutableExtension(string extension)
    {
        return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".com", StringComparison.OrdinalIgnoreCase);
    }
}
