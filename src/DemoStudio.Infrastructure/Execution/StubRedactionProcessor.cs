namespace DemoStudio.Infrastructure.Execution;

using DemoStudio.Redaction.Abstractions.Interfaces;

// Intentional runtime fallback used when redaction is left on the stub implementation.
public sealed class StubRedactionProcessor : IRedactionProcessor
{
    public Task<RedactionResult> ProcessAsync(RedactionRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.RawVideoPath))
        {
            return Task.FromResult(new RedactionResult(false, null, Array.Empty<string>(), $"Raw video file '{request.RawVideoPath}' was not found."));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.RedactedVideoPath) ?? ".");
        File.Copy(request.RawVideoPath, request.RedactedVideoPath, true);

        var lines = new List<string>
        {
            $"REDACTION_STUB|Run={request.Run.Id}|Rules={request.Rules.Count}"
        };

        foreach (var rule in request.Rules)
        {
            lines.Add($"REDACTION_RULE|Name={rule.Name}|Match={rule.MatchExpression}|Replacement={rule.ReplacementText}");
        }

        return Task.FromResult(new RedactionResult(true, request.RedactedVideoPath, lines, null));
    }
}
