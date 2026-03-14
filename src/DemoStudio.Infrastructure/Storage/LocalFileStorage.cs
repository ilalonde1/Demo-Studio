namespace DemoStudio.Infrastructure.Storage;

using DemoStudio.Application.Abstractions.System;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;
    private readonly string _basePathBoundary;

    public LocalFileStorage(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path is required.", nameof(basePath));
        }

        _basePath = Path.GetFullPath(basePath);
        Directory.CreateDirectory(_basePath);
        _basePathBoundary = _basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(relativePath);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return fullPath;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolvePath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolvePath(path);
        return File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var candidate = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_basePath, path));

        if (string.Equals(candidate, _basePath, StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        if (!candidate.StartsWith(_basePathBoundary, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path '{path}' resolves outside the allowed storage root.");
        }

        return candidate;
    }
}
