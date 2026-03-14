using System.IO;
using System.Text.Json;

namespace DemoStudio.Desktop.App.Services;

internal static class DesktopAtomicJsonFile
{
    public static async Task SaveAsync<T>(
        string path,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Target directory is invalid.");
        }

        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var backupPath = fullPath + ".bak";

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static async Task<DesktopAtomicJsonLoadResult<T>> LoadAsync<T>(
        string path,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var backupPath = fullPath + ".bak";

        if (!File.Exists(fullPath))
        {
            if (!File.Exists(backupPath))
            {
                return DesktopAtomicJsonLoadResult<T>.NotFound();
            }

            var backup = await TryReadAsync<T>(backupPath, options, cancellationToken).ConfigureAwait(false);
            if (backup.Succeeded)
            {
                return DesktopAtomicJsonLoadResult<T>.Success(
                    backup.Value!,
                    $"Primary state file '{Path.GetFileName(fullPath)}' was missing; restored from backup.",
                    recoveredFromBackup: true);
            }

            return DesktopAtomicJsonLoadResult<T>.Failure(
                $"State file '{Path.GetFileName(fullPath)}' was missing and backup could not be read: {backup.ErrorMessage}");
        }

        var primary = await TryReadAsync<T>(fullPath, options, cancellationToken).ConfigureAwait(false);
        if (primary.Succeeded)
        {
            return DesktopAtomicJsonLoadResult<T>.Success(primary.Value!);
        }

        if (File.Exists(backupPath))
        {
            var backup = await TryReadAsync<T>(backupPath, options, cancellationToken).ConfigureAwait(false);
            if (backup.Succeeded)
            {
                return DesktopAtomicJsonLoadResult<T>.Success(
                    backup.Value!,
                    $"State file '{Path.GetFileName(fullPath)}' was corrupt; restored last known good backup.",
                    recoveredFromBackup: true);
            }
        }

        return DesktopAtomicJsonLoadResult<T>.Failure(
            $"State file '{Path.GetFileName(fullPath)}' is corrupt and no valid backup is available: {primary.ErrorMessage}");
    }

    public static Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var backupPath = fullPath + ".bak";

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        return Task.CompletedTask;
    }

    private static async Task<ReadAttempt<T>> TryReadAsync<T>(
        string path,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var value = JsonSerializer.Deserialize<T>(raw, options);
            if (value is null)
            {
                return ReadAttempt<T>.Fail("Deserialized value was null.");
            }

            return ReadAttempt<T>.Ok(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return ReadAttempt<T>.Fail(ex.Message);
        }
    }

    private sealed record ReadAttempt<T>(bool Succeeded, T? Value, string? ErrorMessage)
    {
        public static ReadAttempt<T> Ok(T value) => new(true, value, null);

        public static ReadAttempt<T> Fail(string errorMessage) => new(false, default, errorMessage);
    }
}

internal sealed record DesktopAtomicJsonLoadResult<T>(
    bool Exists,
    T? Value,
    string? Diagnostic,
    bool RecoveredFromBackup)
{
    public static DesktopAtomicJsonLoadResult<T> NotFound() => new(false, default, null, false);

    public static DesktopAtomicJsonLoadResult<T> Success(T value, string? diagnostic = null, bool recoveredFromBackup = false)
        => new(true, value, diagnostic, recoveredFromBackup);

    public static DesktopAtomicJsonLoadResult<T> Failure(string diagnostic)
        => new(true, default, diagnostic, false);
}
