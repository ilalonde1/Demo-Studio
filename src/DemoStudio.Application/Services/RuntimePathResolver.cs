namespace DemoStudio.Application.Services;

public static class RuntimePathResolver
{
    private static readonly string[] IgnoredSearchSegments =
    {
        ".git",
        ".vs",
        "node_modules",
        "obj",
        "packages",
        "TestResults"
    };

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

            var resolved = ResolveExecutableWithinDirectory(candidateDirectory, configuredPath);
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

    private static string? ResolveExecutableWithinDirectory(string directory, string configuredPath)
    {
        try
        {
            var candidateFileName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(configuredPath.Trim()));
            var projectName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));

            var searchRoots = EnumerateExecutableSearchRoots(directory).ToArray();
            var exactNamedCandidate = searchRoots
                .SelectMany(root => EnumerateExecutableCandidates(root, maxDepth: GetSearchDepth(root)))
                .Where(path => MatchesPreferredExecutableName(path, candidateFileName, projectName))
                .OrderBy(path => ScoreExecutablePath(path))
                .ThenBy(path => path.Length)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(exactNamedCandidate))
            {
                return exactNamedCandidate;
            }

            return searchRoots
                .SelectMany(root => EnumerateExecutableCandidates(root, maxDepth: GetSearchDepth(root)))
                .OrderBy(path => ScoreExecutablePath(path))
                .ThenBy(path => path.Length)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateExecutableSearchRoots(string directory)
    {
        yield return directory;

        foreach (var root in EnumerateKnownLayoutRoots(directory))
        {
            yield return root;
        }
    }

    private static IEnumerable<string> EnumerateKnownLayoutRoots(string directory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativeRoot in new[]
                 {
                     "publish",
                     "app.publish",
                     Path.Combine("bin", "Debug"),
                     Path.Combine("bin", "Release")
                 })
        {
            string candidateRoot;
            try
            {
                candidateRoot = Path.GetFullPath(Path.Combine(directory, relativeRoot));
            }
            catch
            {
                continue;
            }

            if (Directory.Exists(candidateRoot) && seen.Add(candidateRoot))
            {
                yield return candidateRoot;
            }
        }
    }

    private static int GetSearchDepth(string root)
    {
        var normalized = root.Replace('/', '\\');
        if (normalized.EndsWith("\\bin\\Debug", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("\\bin\\Release", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (normalized.EndsWith("\\publish", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("\\app.publish", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 0;
    }

    private static IEnumerable<string> EnumerateExecutableCandidates(string root, int maxDepth)
    {
        var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Dequeue();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsSupportedExecutableCandidate(file))
                {
                    yield return Path.GetFullPath(file);
                }
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                if (ShouldSkipExecutableSearchDirectory(childDirectory))
                {
                    continue;
                }

                pending.Enqueue((childDirectory, depth + 1));
            }
        }
    }

    private static bool ShouldSkipExecutableSearchDirectory(string directory)
    {
        var name = Path.GetFileName(directory);
        return string.IsNullOrWhiteSpace(name)
               || IgnoredSearchSegments.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesPreferredExecutableName(string path, string? candidateFileName, string? projectName)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(candidateFileName)
            && fileName.Equals(candidateFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            return false;
        }

        var projectExecutableName = projectName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? projectName
            : projectName + ".exe";
        return fileName.Equals(projectExecutableName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedExecutableCandidate(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return !fileName.Equals("apphost.exe", StringComparison.OrdinalIgnoreCase)
               && !fileName.Equals("testhost.exe", StringComparison.OrdinalIgnoreCase)
               && !fileName.EndsWith(".vshost.exe", StringComparison.OrdinalIgnoreCase);
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
