using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using DemoStudio.Application.Abstractions.System;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopPublishPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IProcessLauncher _processLauncher;

    public DesktopPublishPackageService(IProcessLauncher processLauncher)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    }

    public async Task<DesktopPublishPackageResult> CreateAsync(
        DesktopPublishPackageRequest request,
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return DesktopPublishPackageResult.Failure("Publish package failed: request is missing.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceVideoPath) || !File.Exists(request.SourceVideoPath))
        {
            return DesktopPublishPackageResult.Failure("Publish package failed: source video not found.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputRoot))
        {
            return DesktopPublishPackageResult.Failure("Publish package failed: output root is required.");
        }

        Directory.CreateDirectory(request.OutputRoot);
        var packageRoot = Path.Combine(
            request.OutputRoot,
            $"publish-{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{request.SessionId:N}");
        Directory.CreateDirectory(packageRoot);

        var videoOutput = Path.Combine(packageRoot, "demo.mp4");
        File.Copy(request.SourceVideoPath, videoOutput, overwrite: true);

        var thumbnailOutput = Path.Combine(packageRoot, "thumbnail.jpg");
        var thumbnailCreated = await TryGenerateThumbnailAsync(
            request.SourceVideoPath,
            thumbnailOutput,
            ffmpegPath,
            packageRoot,
            cancellationToken);

        var metadata = new DesktopPublishMetadata(
            request.SessionId,
            request.Title,
            request.Description,
            request.QualityPreset,
            request.ExportStyle,
            request.ClipCount,
            request.DurationSeconds,
            request.StartedUtc,
            request.CompletedUtc,
            Path.GetFileName(videoOutput),
            thumbnailCreated ? Path.GetFileName(thumbnailOutput) : null,
            DateTimeOffset.UtcNow);

        var metadataPath = Path.Combine(packageRoot, "metadata.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "share-copy.txt"), DesktopPublishTextBuilder.BuildShareCopy(metadata), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "README.txt"), DesktopPublishTextBuilder.BuildReadme(metadata), cancellationToken);

        var zipPath = Path.Combine(request.OutputRoot, $"publish-{request.SessionId:N}-latest.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(packageRoot, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        return thumbnailCreated
            ? DesktopPublishPackageResult.Success(zipPath)
            : DesktopPublishPackageResult.SuccessWithWarning(zipPath, "Package created, but thumbnail generation was skipped.");
    }

    private async Task<bool> TryGenerateThumbnailAsync(
        string sourceVideo,
        string thumbnailOutput,
        string ffmpegPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return false;
        }

        try
        {
            var args =
                $"-y -ss 00:00:01 -i \"{sourceVideo.Replace("\"", "\\\"", StringComparison.Ordinal)}\" -frames:v 1 -q:v 2 \"{thumbnailOutput.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
            var result = await _processLauncher.LaunchAsync(
                new ProcessLaunchRequest(ffmpegPath, args, workingDirectory),
                cancellationToken);

            return result.Started && result.Execution is not null && result.Execution.ExitCode == 0 && File.Exists(thumbnailOutput);
        }
        catch
        {
            return false;
        }
    }
}

public sealed record DesktopPublishPackageRequest(
    Guid SessionId,
    string SourceVideoPath,
    string OutputRoot,
    string Title,
    string Description,
    string QualityPreset,
    string ExportStyle,
    int ClipCount,
    double DurationSeconds,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc);

public sealed record DesktopPublishMetadata(
    Guid SessionId,
    string Title,
    string Description,
    string QualityPreset,
    string ExportStyle,
    int ClipCount,
    double DurationSeconds,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    string VideoFile,
    string? ThumbnailFile,
    DateTimeOffset PackagedUtc);

internal static class DesktopPublishTextBuilder
{
    public static string BuildShareCopy(DesktopPublishMetadata metadata)
        => $"New demo published: {metadata.Title} | Style: {metadata.ExportStyle} | Quality: {metadata.QualityPreset} | Duration: {TimeSpan.FromSeconds(metadata.DurationSeconds):mm\\:ss}";

    public static string BuildReadme(DesktopPublishMetadata metadata)
        => string.Join(Environment.NewLine, new[]
        {
            "DemoStudio Publish Package",
            $"Title: {metadata.Title}",
            $"Style: {metadata.ExportStyle}",
            $"Quality: {metadata.QualityPreset}",
            $"Duration: {TimeSpan.FromSeconds(metadata.DurationSeconds):mm\\:ss}",
            $"Clip Count: {metadata.ClipCount}",
            $"Video: {metadata.VideoFile}",
            $"Thumbnail: {metadata.ThumbnailFile ?? "-"}",
            $"Packaged UTC: {metadata.PackagedUtc:yyyy-MM-dd HH:mm:ss}"
        });
}

public sealed record DesktopPublishPackageResult(bool Succeeded, string Message, string? PackagePath)
{
    public static DesktopPublishPackageResult Success(string packagePath)
        => new(true, $"Publish package created: {packagePath}", packagePath);

    public static DesktopPublishPackageResult SuccessWithWarning(string packagePath, string warning)
        => new(true, $"Publish package created: {packagePath} ({warning})", packagePath);

    public static DesktopPublishPackageResult Failure(string message)
        => new(false, message, null);
}
