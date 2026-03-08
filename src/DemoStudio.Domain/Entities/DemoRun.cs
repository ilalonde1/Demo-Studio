namespace DemoStudio.Domain.Entities;

using DemoStudio.Domain.Common;
using DemoStudio.Domain.Enums;

public sealed class DemoRun : BaseEntity
{
    private DemoRun() : base(Guid.NewGuid())
    {
    }

    public DemoRun(Guid demoProjectId, Guid demoFlowId, string requestedBy) : base(Guid.NewGuid())
    {
        if (demoProjectId == Guid.Empty)
        {
            throw new ArgumentException("Project identifier is required.", nameof(demoProjectId));
        }

        if (demoFlowId == Guid.Empty)
        {
            throw new ArgumentException("Flow identifier is required.", nameof(demoFlowId));
        }

        if (string.IsNullOrWhiteSpace(requestedBy))
        {
            throw new ArgumentException("RequestedBy is required.", nameof(requestedBy));
        }

        DemoProjectId = demoProjectId;
        DemoFlowId = demoFlowId;
        RequestedBy = requestedBy.Trim();
        Status = DemoRunStatus.Queued;
        QueuedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid DemoProjectId { get; private set; }

    public Guid DemoFlowId { get; private set; }

    public DemoRunStatus Status { get; private set; }

    public string RequestedBy { get; private set; } = string.Empty;

    public DateTimeOffset QueuedAtUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string? OutputDirectory { get; private set; }

    public string? RawVideoPath { get; private set; }

    public string? RedactedVideoPath { get; private set; }

    public string? LogPath { get; private set; }

    public string? OutputVideoPath { get; private set; }

    public string? FailureReason { get; private set; }

    public void MarkRunning(string outputDirectory, string? logPath = null)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        if (Status is not DemoRunStatus.Queued)
        {
            throw new InvalidOperationException($"Run must be in '{DemoRunStatus.Queued}' state to start execution.");
        }

        Status = DemoRunStatus.Running;
        StartedAtUtc = DateTimeOffset.UtcNow;
        OutputDirectory = outputDirectory.Trim();
        LogPath = string.IsNullOrWhiteSpace(logPath) ? null : logPath.Trim();
        FailureReason = null;
        MarkUpdated();
    }

    public void MarkSucceeded(string outputVideoPath, string? rawVideoPath, string? redactedVideoPath, string? logPath)
    {
        if (string.IsNullOrWhiteSpace(outputVideoPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputVideoPath));
        }

        if (Status is not DemoRunStatus.Running)
        {
            throw new InvalidOperationException($"Run must be in '{DemoRunStatus.Running}' state to complete successfully.");
        }

        Status = DemoRunStatus.Succeeded;
        OutputVideoPath = outputVideoPath.Trim();
        RawVideoPath = string.IsNullOrWhiteSpace(rawVideoPath) ? null : rawVideoPath.Trim();
        RedactedVideoPath = string.IsNullOrWhiteSpace(redactedVideoPath) ? null : redactedVideoPath.Trim();
        LogPath = string.IsNullOrWhiteSpace(logPath) ? LogPath : logPath.Trim();
        FailureReason = null;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void MarkFailed(string reason, string? rawVideoPath = null, string? redactedVideoPath = null, string? logPath = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Failure reason is required.", nameof(reason));
        }

        if (Status is not DemoRunStatus.Running and not DemoRunStatus.Queued)
        {
            throw new InvalidOperationException("Run can be marked as failed only from Queued or Running state.");
        }

        Status = DemoRunStatus.Failed;
        RawVideoPath = string.IsNullOrWhiteSpace(rawVideoPath) ? RawVideoPath : rawVideoPath.Trim();
        RedactedVideoPath = string.IsNullOrWhiteSpace(redactedVideoPath) ? RedactedVideoPath : redactedVideoPath.Trim();
        LogPath = string.IsNullOrWhiteSpace(logPath) ? LogPath : logPath.Trim();
        FailureReason = reason.Trim();
        CompletedAtUtc = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void SetOutputArtifacts(string outputDirectory, string? rawVideoPath, string? redactedVideoPath, string? logPath)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        if (Status is DemoRunStatus.Cancelled)
        {
            throw new InvalidOperationException("Cannot set output artifacts for a cancelled run.");
        }

        OutputDirectory = outputDirectory.Trim();
        RawVideoPath = string.IsNullOrWhiteSpace(rawVideoPath) ? null : rawVideoPath.Trim();
        RedactedVideoPath = string.IsNullOrWhiteSpace(redactedVideoPath) ? null : redactedVideoPath.Trim();
        LogPath = string.IsNullOrWhiteSpace(logPath) ? null : logPath.Trim();
        MarkUpdated();
    }

    public void MarkCancelled(string reason)
    {
        if (Status is DemoRunStatus.Succeeded or DemoRunStatus.Failed)
        {
            throw new InvalidOperationException("Completed runs cannot be cancelled.");
        }

        Status = DemoRunStatus.Cancelled;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled" : reason.Trim();
        CompletedAtUtc = DateTimeOffset.UtcNow;
        MarkUpdated();
    }
}
