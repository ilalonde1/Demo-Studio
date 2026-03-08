namespace DemoStudio.Redaction.Abstractions.Interfaces;

using DemoStudio.Domain.Entities;

public interface IRedactionProcessor
{
    Task<RedactionResult> ProcessAsync(RedactionRequest request, CancellationToken cancellationToken = default);
}

public sealed record RedactionRequest(
    DemoRun Run,
    string RawVideoPath,
    string RedactedVideoPath,
    IReadOnlyCollection<RedactionRule> Rules);

public sealed record RedactionResult(bool Succeeded, string? RedactedOutputPath, IReadOnlyCollection<string> LogLines, string? ErrorMessage);
