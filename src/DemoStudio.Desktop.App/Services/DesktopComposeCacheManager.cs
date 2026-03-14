using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

internal sealed class DesktopComposeCacheManager
{
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(14);
    private static readonly TimeSpan CachePruneInterval = TimeSpan.FromMinutes(30);
    private const long CacheMaxBytes = 2L * 1024 * 1024 * 1024;
    private const long CacheTrimTargetBytes = (long)(CacheMaxBytes * 0.85);

    private readonly ILogger _logger;

    public DesktopComposeCacheManager(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsUsableFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            return info.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed checking file usability for {Path}.", path);
            return false;
        }
    }

    public string BuildFileSignature(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return string.Empty;
            }

            return $"{path.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed building file signature for {Path}.", path);
            return string.Empty;
        }
    }

    public string HashToken(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void DeleteDirectory(string path, string? composeOperationId = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed deleting compose temp directory {Path}. ComposeOperationId={ComposeOperationId}", path, composeOperationId);
        }
    }

    public void Prune(string cacheDirectory, DateTimeOffset nowUtc, string? composeOperationId = null)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
        {
            return;
        }

        try
        {
            var stampPath = Path.Combine(cacheDirectory, ".prune.stamp");
            if (File.Exists(stampPath))
            {
                var stampAge = nowUtc - File.GetLastWriteTimeUtc(stampPath);
                if (stampAge < CachePruneInterval)
                {
                    return;
                }
            }

            var expirationUtc = nowUtc - CacheMaxAge;
            var root = new DirectoryInfo(cacheDirectory);
            var files = root
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}tmp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(file => !string.Equals(file.Name, ".prune.stamp", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in files)
            {
                if (file.LastWriteTimeUtc < expirationUtc.UtcDateTime)
                {
                    TryDeleteFile(file.FullName, composeOperationId);
                }
            }

            files = root
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}tmp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(file => !string.Equals(file.Name, ".prune.stamp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToList();

            long totalBytes = 0;
            foreach (var file in files)
            {
                totalBytes += SafeLength(file);
            }

            if (totalBytes > CacheMaxBytes)
            {
                foreach (var file in files)
                {
                    var length = SafeLength(file);
                    TryDeleteFile(file.FullName, composeOperationId);
                    totalBytes -= length;
                    if (totalBytes <= CacheTrimTargetBytes)
                    {
                        break;
                    }
                }
            }

            TryDeleteEmptyDirectories(cacheDirectory, composeOperationId);
            File.WriteAllText(stampPath, nowUtc.ToString("O", CultureInfo.InvariantCulture), Encoding.UTF8);
            File.SetLastWriteTimeUtc(stampPath, nowUtc.UtcDateTime);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Compose cache prune failed for {CacheDirectory}. ComposeOperationId={ComposeOperationId}", cacheDirectory, composeOperationId);
        }
    }

    public ComposeCacheStats GetStats(string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
        {
            return new ComposeCacheStats(0, 0, null, null);
        }

        try
        {
            var files = Directory
                .EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories)
                .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}tmp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.EndsWith(".prune.stamp", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .ToArray();
            if (files.Length == 0)
            {
                return new ComposeCacheStats(0, 0, null, null);
            }

            var totalBytes = files.Sum(SafeLength);
            var oldest = files.Min(file => file.LastWriteTimeUtc);
            var newest = files.Max(file => file.LastWriteTimeUtc);
            return new ComposeCacheStats(files.Length, totalBytes, oldest, newest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading compose cache stats for {CacheDirectory}.", cacheDirectory);
            return new ComposeCacheStats(0, 0, null, null);
        }
    }

    private long SafeLength(FileInfo file)
    {
        try
        {
            return file.Exists ? file.Length : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed reading file length for {Path}.", file.FullName);
            return 0;
        }
    }

    private void TryDeleteFile(string path, string? composeOperationId = null)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed deleting compose cache file {Path}. ComposeOperationId={ComposeOperationId}", path, composeOperationId);
        }
    }

    private void TryDeleteEmptyDirectories(string rootPath, string? composeOperationId = null)
    {
        try
        {
            var dirs = Directory
                .EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                .OrderByDescending(x => x.Length)
                .ToArray();
            foreach (var dir in dirs)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir, false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed deleting empty compose cache directory {DirectoryPath}. ComposeOperationId={ComposeOperationId}", dir, composeOperationId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed enumerating compose cache directories under {RootPath}. ComposeOperationId={ComposeOperationId}", rootPath, composeOperationId);
        }
    }
}

internal sealed record ComposeCacheStats(
    int FileCount,
    long TotalBytes,
    DateTimeOffset? OldestWriteUtc,
    DateTimeOffset? NewestWriteUtc);
