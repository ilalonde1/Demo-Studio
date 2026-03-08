namespace DemoStudio.Application.Services;

public static class RuntimePathResolver
{
    public static bool TryResolveFile(string configuredPath, out string fullPath, string? anchorPath = null)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return false;
        }

        if (Path.IsPathRooted(configuredPath))
        {
            var rooted = Path.GetFullPath(configuredPath);
            if (File.Exists(rooted))
            {
                fullPath = rooted;
                return true;
            }

            return false;
        }

        foreach (var baseDirectory in EnumerateCandidateBaseDirectories(anchorPath))
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDirectory, configuredPath));
            if (File.Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<string> EnumerateCandidateBaseDirectories(string? anchorPath = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string?>();

        if (!string.IsNullOrWhiteSpace(anchorPath))
        {
            roots.Add(anchorPath);
        }

        roots.Add(Environment.CurrentDirectory);
        roots.Add(AppContext.BaseDirectory);

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            roots.Add(Path.GetDirectoryName(Environment.ProcessPath));
        }

        var chains = roots
            .SelectMany(EnumerateSelfAndParents)
            .ToArray();

        foreach (var solutionRoot in chains.Where(ContainsSolutionMarker))
        {
            if (seen.Add(solutionRoot))
            {
                yield return solutionRoot;
            }
        }

        foreach (var directory in chains)
        {
            if (seen.Add(directory))
            {
                yield return directory;
            }
        }
    }

    public static bool TryResolveDesktopExecutable(string configuredPath, out string fullPath, string? anchorPath = null)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return false;
        }

        if (TryResolveFile(configuredPath, out fullPath, anchorPath))
        {
            return true;
        }

        foreach (var baseDirectory in EnumerateCandidateBaseDirectories(anchorPath))
        {
            string candidateDirectory;
            try
            {
                candidateDirectory = Path.IsPathRooted(configuredPath)
                    ? Path.GetFullPath(configuredPath)
                    : Path.GetFullPath(Path.Combine(baseDirectory, configuredPath));
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(candidateDirectory))
            {
                continue;
            }

            var resolved = ResolveExecutableWithinDirectory(candidateDirectory);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                fullPath = resolved;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateSelfAndParents(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            yield break;
        }

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch
        {
            yield break;
        }

        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static bool ContainsSolutionMarker(string directory)
    {
        try
        {
            return File.Exists(Path.Combine(directory, "DemoStudio.sln"));
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveExecutableWithinDirectory(string directory)
    {
        try
        {
            var candidates = Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var fileName = Path.GetFileName(path);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        return false;
                    }

                    return !fileName.Equals("apphost.exe", StringComparison.OrdinalIgnoreCase)
                        && !fileName.Equals("testhost.exe", StringComparison.OrdinalIgnoreCase)
                        && !fileName.EndsWith(".vshost.exe", StringComparison.OrdinalIgnoreCase);
                })
                .Select(path => Path.GetFullPath(path))
                .OrderBy(path => ScoreExecutablePath(path))
                .ThenBy(path => path.Length)
                .FirstOrDefault();

            return candidates;
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreExecutablePath(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (normalized.Contains("\\bin\\Debug\\", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (normalized.Contains("\\bin\\Release\\", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (normalized.Contains("\\publish\\", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }
}
