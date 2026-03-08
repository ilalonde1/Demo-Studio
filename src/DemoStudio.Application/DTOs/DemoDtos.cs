namespace DemoStudio.Application.DTOs;

using DemoStudio.Domain.Enums;

public sealed record DemoProjectDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);

public sealed record DemoRunDto(
    Guid Id,
    Guid DemoProjectId,
    Guid DemoFlowId,
    DemoRunStatus Status,
    string RequestedBy,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? OutputVideoPath,
    string? FailureReason,
    string? OutputDirectory,
    string? RawVideoPath,
    string? RedactedVideoPath,
    string? LogPath);

public sealed record DemoFlowDto(
    Guid Id,
    Guid DemoProjectId,
    string Name,
    int Version,
    bool IsDeterministic,
    int StepCount);
